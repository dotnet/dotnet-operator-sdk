// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.LeaderElection;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Constants;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Test.Watcher;

[Trait("Area", "ScopedLeaderElection")]
public sealed class ScopeAwareResourceWatcherTest
{
    private readonly Mock<ILeadershipScope> _scope = new();
    private readonly Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> _queue = new();
    private readonly Mock<IKubernetesClient> _client = new();

    public ScopeAwareResourceWatcherTest()
    {
        _queue
            .Setup(q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(),
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Should_Skip_Event_When_Not_Responsible()
    {
        _scope
            .Setup(s => s.IsResponsibleForAsync(
                It.IsAny<IKubernetesObject<V1ObjectMeta>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        using var watcher = CreateWatcher();

        await watcher.InvokeOnEventAsync(
            WatchEventType.Modified,
            CreateEntity("foreign-namespace"),
            TestContext.Current.CancellationToken);

        _queue.Verify(
            q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(),
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_Enqueue_Event_When_Responsible()
    {
        var entity = CreateEntity("owned-namespace");
        _scope
            .Setup(s => s.IsResponsibleForAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var watcher = CreateWatcher();

        await watcher.InvokeOnEventAsync(
            WatchEventType.Modified,
            entity,
            TestContext.Current.CancellationToken);

        _queue.Verify(
            q => q.Enqueue(
                entity,
                ReconciliationType.Modified,
                ReconciliationTriggerSource.ApiServer,
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_Resync_Through_Regular_Event_Path_On_Scope_Change()
    {
        var entity = CreateEntity("acquired-namespace");
        var enqueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scope
            .Setup(s => s.IsResponsibleForAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _client
            .Setup(c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                null,
                "app=test",
                "metadata.name=test-entity",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([entity]);
        _queue
            .Setup(q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(),
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() => enqueued.TrySetResult());
        using var watcher = CreateWatcher();
        await watcher.StartAsync(TestContext.Current.CancellationToken);

        _scope.Raise(s => s.ScopeChanged += null);

        await enqueued.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        _queue.Verify(
            q => q.Enqueue(
                entity,
                ReconciliationType.Modified,
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        await watcher.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Reconcile_Reacquired_Entity_At_Unchanged_Generation_On_Scope_Change()
    {
        // Regression: owned -> lost -> reacquired at an unchanged generation. A prior ownership term
        // left this entity's deduplication token in the cache at generation 1. When the entity is
        // handed back, the scope resync must still reconcile it - otherwise the takeover reconcile is
        // silently suppressed by the generation check and never runs.
        var entity = new V1OperatorIntegrationTestEntity
        {
            Metadata = new V1ObjectMeta
            {
                Name = "test-entity",
                NamespaceProperty = "reacquired-namespace",
                Uid = "uid-1",
                Generation = 1,
            },
        };
        _scope
            .Setup(s => s.IsResponsibleForAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _client
            .Setup(c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                null,
                "app=test",
                "metadata.name=test-entity",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([entity]);
        var enqueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue
            .Setup(q => q.Enqueue(
                entity,
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() => enqueued.TrySetResult());

        var cacheProvider = new ServiceCollection()
            .WithResourceWatcherEntityCaching(
                new OperatorSettingsBuilder { ReconcileStrategy = ReconcileStrategy.ByGeneration }.Build())
            .BuildServiceProvider()
            .GetRequiredService<IFusionCacheProvider>();
        var cache = cacheProvider.GetCache(CacheConstants.CacheNames.ResourceWatcher);
        var token = TestContext.Current.CancellationToken;

        // Simulate the earlier ownership term: the dedup token exists at generation 1, tagged with the
        // entity type so the scope resync can drop it.
        await cache.SetAsync(
            "uid-1",
            1L,
            tags: [typeof(V1OperatorIntegrationTestEntity).FullName!],
            token: token);

        using var watcher = CreateWatcher(cache: cache);
        await watcher.StartAsync(token);

        _scope.Raise(s => s.ScopeChanged += null);

        await enqueued.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        _queue.Verify(
            q => q.Enqueue(
                entity,
                ReconciliationType.Modified,
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        await watcher.StopAsync(token);
    }

    [Fact]
    public async Task Should_Resync_Within_The_Configured_Watch_Namespace()
    {
        var listed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client
            .Setup(c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                "watched-namespace",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .Callback(() => listed.TrySetResult());
        using var watcher = CreateWatcher(new OperatorSettingsBuilder { Namespace = "watched-namespace" }.Build());
        await watcher.StartAsync(TestContext.Current.CancellationToken);

        _scope.Raise(s => s.ScopeChanged += null);

        await listed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        _client.Verify(
            c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                "watched-namespace",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        await watcher.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Coalesce_Rapid_Scope_Changes_Into_At_Most_One_Followup_Resync()
    {
        var firstListStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listCalls = 0;
        _client
            .Setup(c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                null,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref listCalls) == 1)
                {
                    firstListStarted.TrySetResult();
                    await listGate.Task;
                }

                return (IList<V1OperatorIntegrationTestEntity>)[];
            });
        using var watcher = CreateWatcher();
        await watcher.StartAsync(TestContext.Current.CancellationToken);

        // First signal starts the resync; the following signals arrive while it is blocked in the
        // list call and must fold into a single follow-up pass.
        _scope.Raise(s => s.ScopeChanged += null);
        await firstListStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        _scope.Raise(s => s.ScopeChanged += null);
        _scope.Raise(s => s.ScopeChanged += null);
        _scope.Raise(s => s.ScopeChanged += null);
        listGate.TrySetResult();

        // Wait until the coalesced follow-up pass completed (2 calls total), then ensure no more follow.
        await WaitUntilAsync(() => Volatile.Read(ref listCalls) == 2, TimeSpan.FromSeconds(5));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Volatile.Read(ref listCalls).Should().Be(2);

        await watcher.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Serialize_Watch_Events_With_A_Running_Resync()
    {
        var resyncEntity = CreateEntity("resync-namespace");
        var enqueueStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueueGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scope
            .Setup(s => s.IsResponsibleForAsync(
                It.IsAny<IKubernetesObject<V1ObjectMeta>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _client
            .Setup(c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                null,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([resyncEntity]);
        _queue
            .Setup(q => q.Enqueue(
                resyncEntity,
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                enqueueStarted.TrySetResult();
                await enqueueGate.Task;
                return true;
            });
        using var watcher = CreateWatcher();
        await watcher.StartAsync(TestContext.Current.CancellationToken);

        // Block the resync inside the event pipeline, then let a watch event arrive concurrently:
        // it must not enter the pipeline before the resync's event left it.
        _scope.Raise(s => s.ScopeChanged += null);
        await enqueueStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var watchEvent = watcher.InvokeOnEventAsync(
            WatchEventType.Modified,
            CreateEntity("event-namespace"),
            TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        watchEvent.IsCompleted.Should().BeFalse();

        enqueueGate.TrySetResult();
        await watchEvent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await watcher.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Drain_Running_Resync_Before_Disposing()
    {
        var listStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client
            .Setup(c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                null,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                listStarted.TrySetResult();

                // Ignores cancellation on purpose: dispose must wait even for a dependency that
                // does not honor the token.
                await listGate.Task;
                return (IList<V1OperatorIntegrationTestEntity>)[];
            });
        var watcher = CreateWatcher();
        await watcher.StartAsync(TestContext.Current.CancellationToken);

        _scope.Raise(s => s.ScopeChanged += null);
        await listStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var disposeTask = watcher.DisposeAsync().AsTask();

        await Task.Delay(100, TestContext.Current.CancellationToken);
        disposeTask.IsCompleted.Should().BeFalse("dispose must wait for the running resync to drain");

        listGate.TrySetResult();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Ignore_Scope_Callback_That_Arrives_After_Dispose()
    {
        // Models a publisher that copied the invocation list before the watcher unsubscribed:
        // the captured delegate is invoked directly, bypassing the unsubscription.
        Action? capturedHandler = null;
        _scope
            .SetupAdd(s => s.ScopeChanged += It.IsAny<Action>())
            .Callback<Action>(handler => capturedHandler = handler);
        var watcher = CreateWatcher();
        await watcher.StartAsync(TestContext.Current.CancellationToken);
        capturedHandler.Should().NotBeNull();

        await watcher.DisposeAsync();
        capturedHandler!.Invoke();

        // No resync may start on the disposed resources.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        _client.Verify(
            c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    private static V1OperatorIntegrationTestEntity CreateEntity(string @namespace)
        => new()
        {
            Metadata = new V1ObjectMeta { Name = "test-entity", NamespaceProperty = @namespace, Uid = "uid-1" },
        };

    private TestableWatcher CreateWatcher(OperatorSettings? settings = null, IFusionCache? cache = null)
    {
        var cacheProvider = Mock.Of<IFusionCacheProvider>();
        Mock.Get(cacheProvider)
            .Setup(cp => cp.GetCache(It.IsAny<string>()))
            .Returns(cache ?? Mock.Of<IFusionCache>());

        var labelSelector = new Mock<IEntityLabelSelector<V1OperatorIntegrationTestEntity>>();
        labelSelector
            .Setup(s => s.GetLabelSelectorAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<string?>("app=test"));
        var fieldSelector = new Mock<IEntityFieldSelector<V1OperatorIntegrationTestEntity>>();
        fieldSelector
            .Setup(s => s.GetFieldSelectorAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<string?>("metadata.name=test-entity"));

        return new TestableWatcher(
            cacheProvider,
            _queue.Object,
            settings ?? new OperatorSettingsBuilder().Build(),
            labelSelector.Object,
            fieldSelector.Object,
            _client.Object,
            _scope.Object);
    }

    private sealed class TestableWatcher(
        IFusionCacheProvider cacheProvider,
        ITimedEntityQueue<V1OperatorIntegrationTestEntity> queue,
        OperatorSettings settings,
        IEntityLabelSelector<V1OperatorIntegrationTestEntity> labelSelector,
        IEntityFieldSelector<V1OperatorIntegrationTestEntity> fieldSelector,
        IKubernetesClient client,
        ILeadershipScope leadershipScope)
        : ScopeAwareResourceWatcher<V1OperatorIntegrationTestEntity>(
            new ActivitySource("test"),
            Mock.Of<ILogger<ScopeAwareResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            cacheProvider,
            queue,
            settings,
            labelSelector,
            fieldSelector,
            client,
            leadershipScope,
            Mock.Of<IEntityLoggingScopeFactory<V1OperatorIntegrationTestEntity>>())
    {
        public Task InvokeOnEventAsync(
            WatchEventType eventType,
            V1OperatorIntegrationTestEntity entity,
            CancellationToken cancellationToken)
            => OnEventAsync(eventType, entity, cancellationToken);
    }
}
