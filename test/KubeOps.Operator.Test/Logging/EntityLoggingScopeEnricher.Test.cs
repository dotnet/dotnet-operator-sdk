// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Logging;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Test.Logging;

[Trait("Area", "Logging")]
public sealed class EntityLoggingScopeEnricherTest
{
    private static V1OperatorIntegrationTestEntity Entity => new("name", "username", "ns");

    [Fact]
    public void Pipeline_Applies_Registered_Enrichers()
    {
        var pipeline = Pipeline([
            new SetTypedEnricher("first", _ => "value"),
            new SetTypedEnricher("second", e => e.Metadata.Name),
        ]);

        var items = FreshItems();
        pipeline.Enrich(Entity, EntityLoggingPhase.Reconcile, items);

        items["first"].Should().Be("value");
        items["second"].Should().Be("name");
    }

    [Fact]
    public void Pipeline_Uses_First_Writer_Wins()
    {
        var pipeline = Pipeline([
            new SetTypedEnricher("shared", _ => "first"),
            new SetTypedEnricher("shared", _ => "second"),
        ]);

        var items = FreshItems();
        pipeline.Enrich(Entity, EntityLoggingPhase.Reconcile, items);

        items["shared"].Should().Be("first");
    }

    [Fact]
    public void Enricher_Cannot_Override_Builtin_Key()
    {
        var pipeline = Pipeline([new SetTypedEnricher("Name", _ => "overridden")]);

        var scope = Factory(pipeline).CreateFor(WatchEventType.Modified, Entity);
        var items = scope.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        items["Name"].Should().Be("name");
        items.Should().ContainKeys("EventType", "Kind", "Namespace", "Uid", "ResourceVersion");
    }

    [Theory]
    [InlineData(EntityLoggingPhase.Watch)]
    [InlineData(EntityLoggingPhase.Reconcile)]
    public void Pipeline_Passes_The_Current_Phase_To_Enrichers(EntityLoggingPhase phase)
    {
        EntityLoggingPhase? observed = null;
        var pipeline = Pipeline([
            new DelegatingTypedEnricher<V1OperatorIntegrationTestEntity>((_, currentPhase, _) =>
                observed = currentPhase),
        ]);

        pipeline.Enrich(Entity, phase, FreshItems());

        observed.Should().Be(phase);
    }

    [Fact]
    public void A_Throwing_Enricher_Is_Isolated_And_Following_Enrichers_Still_Run()
    {
        var pipeline = Pipeline([
            new ThrowingAfterAddEnricher(),
            new SetTypedEnricher("after", _ => "ran"),
        ]);

        var items = FreshItems();
        var act = () => pipeline.Enrich(Entity, EntityLoggingPhase.Reconcile, items);

        act.Should().NotThrow();
        items.Should().NotContainKey("partial");
        items["after"].Should().Be("ran");
    }

    [Fact]
    public void CreateFor_Enriches_The_Watch_Scope()
    {
        var pipeline = Pipeline([new SetTypedEnricher("custom", _ => "value")]);

        var scope = Factory(pipeline).CreateFor(WatchEventType.Added, Entity);

        scope.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)["custom"].Should().Be("value");
    }

    [Fact]
    public void CreateFor_With_Empty_Pipeline_Preserves_The_Unenriched_Scope()
    {
        var expected = EntityLoggingScope.CreateFor(
            ReconciliationType.Modified,
            ReconciliationTriggerSource.ApiServer,
            Entity).ToDictionary();
        var scope = Factory(Pipeline([])).CreateFor(
            ReconciliationType.Modified,
            ReconciliationTriggerSource.ApiServer,
            Entity).ToDictionary();

        scope.Should().Equal(expected);
    }

    [Fact]
    public async Task ResourceWatcher_Forwards_The_Pipeline_To_The_Watch_Scope()
    {
        var entity = Entity;
        var pipeline = Pipeline([new SetTypedEnricher("watch-custom", _ => "value")]);
        var logger = new CapturingLogger<ResourceWatcher<V1OperatorIntegrationTestEntity>>();
        var client = new Mock<IKubernetesClient>();
        client
            .Setup(c => c.WatchAsync<V1OperatorIntegrationTestEntity>(
                "unit-test",
                null,
                null,
                null,
                true,
                It.IsAny<CancellationToken>()))
            .Returns<string?, string?, string?, string?, bool?, CancellationToken>(
                (_, _, _, _, _, cancellationToken) => WatchOnce(entity, cancellationToken));

        var cacheProvider = new Mock<IFusionCacheProvider>();
        cacheProvider.Setup(c => c.GetCache(It.IsAny<string>())).Returns(Mock.Of<IFusionCache>());

        await using var watcher = new ResourceWatcher<V1OperatorIntegrationTestEntity>(
            new ActivitySource("test"),
            logger,
            cacheProvider.Object,
            Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>(),
            new OperatorSettingsBuilder { Namespace = "unit-test" }.Build(),
            new DefaultEntityLabelSelector<V1OperatorIntegrationTestEntity>(),
            new DefaultEntityFieldSelector<V1OperatorIntegrationTestEntity>(),
            client.Object,
            scopeFactory: Factory(pipeline));

        await watcher.StartAsync(TestContext.Current.CancellationToken);
        var scope = await logger.ScopeCaptured.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await watcher.StopAsync(TestContext.Current.CancellationToken);

        scope["watch-custom"].Should().Be("value");
    }

    [Fact]
    public async Task EntityQueueBackgroundService_Enriches_The_Reconcile_Scope_With_The_Queued_Snapshot()
    {
        var queuedEntity = new V1ConfigMap
        {
            Kind = V1ConfigMap.KubeKind,
            Metadata = new()
            {
                Name = "name",
                NamespaceProperty = "ns",
                Uid = Guid.NewGuid().ToString(),
                ResourceVersion = "1",
            },
        };
        var currentEntity = new V1ConfigMap
        {
            Kind = V1ConfigMap.KubeKind,
            Metadata = new()
            {
                Name = "name",
                NamespaceProperty = "ns",
                Uid = queuedEntity.Metadata.Uid,
                ResourceVersion = "2",
            },
        };
        var enrichmentCount = 0;
        var pipeline = new EntityLoggingScopeEnricherPipeline<V1ConfigMap>(
            [new DelegatingTypedEnricher<V1ConfigMap>((entity, _, properties) =>
            {
                enrichmentCount++;
                properties.TryAdd("reconcile-custom", entity.ResourceVersion());
            })],
            NullLogger<EntityLoggingScopeEnricherPipeline<V1ConfigMap>>.Instance);
        var client = new Mock<IKubernetesClient>();
        client
            .Setup(c => c.GetAsync<V1ConfigMap>("name", "ns", It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentEntity);
        var reconciler = new Mock<IReconciler<V1ConfigMap>>();
        reconciler
            .Setup(r => r.Reconcile(It.IsAny<ReconciliationContext<V1ConfigMap>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1ConfigMap>.Success(currentEntity));

        var logger = new ScopeRecordingLogger<EntityQueueBackgroundService<V1ConfigMap>>();
        var settings = new OperatorSettingsBuilder().Build();
        await using var service = new EntityQueueBackgroundService<V1ConfigMap>(
            new ActivitySource("test"),
            client.Object,
            settings,
            new SingleEntryQueue<V1ConfigMap>(
                new(queuedEntity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer, RetryCount: 0)),
            reconciler.Object,
            new EntityReconcileCoordinator<V1ConfigMap>(settings),
            logger,
            scopeFactory: new EntityLoggingScopeFactory<V1ConfigMap>(pipeline));

        await service.StartAsync(TestContext.Current.CancellationToken);
        var scope = await logger.StartingReconciliationScope.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        scope["ResourceVersion"].Should().Be("1");
        scope["reconcile-custom"].Should().Be("1");
        enrichmentCount.Should().Be(1);
        reconciler.Verify(
            r => r.Reconcile(
                It.Is<ReconciliationContext<V1ConfigMap>>(c => ReferenceEquals(c.Entity, currentEntity)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EntityQueueBackgroundService_Keeps_NotFound_Skip_Log_In_The_Snapshot_Scope()
    {
        var queuedEntity = new V1ConfigMap
        {
            Kind = V1ConfigMap.KubeKind,
            Metadata = new()
            {
                Name = "name",
                NamespaceProperty = "ns",
                Uid = Guid.NewGuid().ToString(),
                ResourceVersion = "1",
            },
        };
        var pipeline = new EntityLoggingScopeEnricherPipeline<V1ConfigMap>(
            [new DelegatingTypedEnricher<V1ConfigMap>((entity, _, properties) =>
                properties.TryAdd("reconcile-custom", entity.ResourceVersion()))],
            NullLogger<EntityLoggingScopeEnricherPipeline<V1ConfigMap>>.Instance);
        var client = new Mock<IKubernetesClient>();
        client
            .Setup(c => c.GetAsync<V1ConfigMap>("name", "ns", It.IsAny<CancellationToken>()))
            .ReturnsAsync((V1ConfigMap?)null);
        var logger = new ScopeRecordingLogger<EntityQueueBackgroundService<V1ConfigMap>>();
        var settings = new OperatorSettingsBuilder().Build();
        await using var service = new EntityQueueBackgroundService<V1ConfigMap>(
            new ActivitySource("test"),
            client.Object,
            settings,
            new SingleEntryQueue<V1ConfigMap>(
                new(queuedEntity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer, RetryCount: 0)),
            Mock.Of<IReconciler<V1ConfigMap>>(),
            new EntityReconcileCoordinator<V1ConfigMap>(settings),
            logger,
            scopeFactory: new EntityLoggingScopeFactory<V1ConfigMap>(pipeline));

        await service.StartAsync(TestContext.Current.CancellationToken);
        var scope = await logger.NotFoundScope.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        scope["ResourceVersion"].Should().Be("1");
        scope["reconcile-custom"].Should().Be("1");
    }

    [Fact]
    public void Builder_Registers_A_Factory_That_Composes_The_Configured_Enrichers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new OperatorBuilder(services, new OperatorSettingsBuilder().WithName("test").Build());

        builder.AddEntityLoggingScopeEnricher<V1OperatorIntegrationTestEntity, TypedRegisteredEnricher>();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IEntityLoggingScopeFactory<V1OperatorIntegrationTestEntity>>();
        var items = factory
            .CreateFor(ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer, Entity)
            .ToDictionary();

        items.Should().ContainKey("registered-typed");
    }

    private static Dictionary<string, object> FreshItems() => new();

    private static async IAsyncEnumerable<(WatchEventType Type, V1OperatorIntegrationTestEntity Entity)> WatchOnce(
        V1OperatorIntegrationTestEntity entity,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return (WatchEventType.Modified, entity);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private static EntityLoggingScopeEnricherPipeline<V1OperatorIntegrationTestEntity> Pipeline(
        IEnumerable<IEntityLoggingScopeEnricher<V1OperatorIntegrationTestEntity>> enrichers)
        => new(enrichers, NullLogger<EntityLoggingScopeEnricherPipeline<V1OperatorIntegrationTestEntity>>.Instance);

    private static EntityLoggingScopeFactory<V1OperatorIntegrationTestEntity> Factory(
        EntityLoggingScopeEnricherPipeline<V1OperatorIntegrationTestEntity> pipeline)
        => new(pipeline);

    private sealed class ThrowingAfterAddEnricher
        : IEntityLoggingScopeEnricher<V1OperatorIntegrationTestEntity>
    {
        public void Enrich(
            V1OperatorIntegrationTestEntity entity,
            EntityLoggingPhase phase,
            IDictionary<string, object> properties)
        {
            properties.TryAdd("partial", "discarded");
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class SetTypedEnricher(string key, Func<V1OperatorIntegrationTestEntity, object> value)
        : IEntityLoggingScopeEnricher<V1OperatorIntegrationTestEntity>
    {
        public void Enrich(
            V1OperatorIntegrationTestEntity entity,
            EntityLoggingPhase phase,
            IDictionary<string, object> properties) =>
            properties.TryAdd(key, value(entity));
    }

    private sealed class DelegatingTypedEnricher<TEntity>(
        Action<TEntity, EntityLoggingPhase, IDictionary<string, object>> action)
        : IEntityLoggingScopeEnricher<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        public void Enrich(
            TEntity entity,
            EntityLoggingPhase phase,
            IDictionary<string, object> properties) =>
            action(entity, phase, properties);
    }

    private sealed class TypedRegisteredEnricher : IEntityLoggingScopeEnricher<V1OperatorIntegrationTestEntity>
    {
        public void Enrich(
            V1OperatorIntegrationTestEntity entity,
            EntityLoggingPhase phase,
            IDictionary<string, object> properties) =>
            properties.TryAdd("registered-typed", true);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public TaskCompletionSource<IReadOnlyDictionary<string, object>> ScopeCaptured { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object>> values)
            {
                ScopeCaptured.TrySetResult(values.ToDictionary());
            }

            return EmptyDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class ScopeRecordingLogger<T> : ILogger<T>
    {
        private readonly AsyncLocal<IReadOnlyDictionary<string, object>?> _currentScope = new();

        public TaskCompletionSource<IReadOnlyDictionary<string, object>> StartingReconciliationScope { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<IReadOnlyDictionary<string, object>> NotFoundScope { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            var previousScope = _currentScope.Value;
            _currentScope.Value = state is IEnumerable<KeyValuePair<string, object>> values
                ? values.ToDictionary()
                : null;
            return new ScopeRestorer(() => _currentScope.Value = previousScope);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (_currentScope.Value is not { } scope)
            {
                return;
            }

            var message = formatter(state, exception);
            if (message.StartsWith("Starting reconciliation", StringComparison.Ordinal))
            {
                StartingReconciliationScope.TrySetResult(scope);
            }

            if (message.Contains("was not found", StringComparison.Ordinal))
            {
                NotFoundScope.TrySetResult(scope);
            }
        }
    }

    private sealed class ScopeRestorer(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private sealed class SingleEntryQueue<TEntity>(QueueEntry<TEntity> entry) : ITimedEntityQueue<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        public Task<bool> Enqueue(
            TEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public async IAsyncEnumerator<QueueEntry<TEntity>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            yield return entry;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Dispose()
        {
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
