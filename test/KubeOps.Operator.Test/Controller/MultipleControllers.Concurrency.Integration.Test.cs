// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Controller;

/// <summary>
/// Regression for the cross-pipeline concurrency blocker: with multiple controllers for the same entity,
/// a per-entity-type coordinator must serialize reconciliation and finalization per object UID, so an
/// entity-global finalizer runs exactly once (and never concurrently) even when two controllers match.
/// </summary>
public sealed class MultipleControllersFinalizerIntegrationTest : IntegrationTestBase
{
    // Must equal IEntityFinalizer.GetIdentifierName for TrackedFinalizer ("{group}/{typename}"), because
    // auto-attach uses the derived name and finalizer removal resolves the keyed finalizer by it.
    private const string FinalizerId = "operator.test/trackedfinalizer";

    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly SideEffectTracker _tracker = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task Should_Run_The_Shared_Finalizer_Once_And_Never_Concurrently()
    {
        var ct = TestContext.Current.CancellationToken;

        // Both controllers match the object and reconcile it, attaching the shared finalizer.
        _mock.TargetInvocationCount = 2;
        var entity = new V1OperatorIntegrationTestEntity("finalized", "username", _ns.Namespace);
        entity.Metadata.Labels = new Dictionary<string, string>
        {
            ["is-managed"] = "true",
            ["autogen-config"] = "true",
        };

        var created = await _client.CreateAsync(entity, ct);
        await _mock.WaitForInvocations;

        // Deleting triggers finalization. If the finalizer is attached (it is, auto-attached during the
        // reconciles above), the object only disappears once the finalizer has run and removed itself.
        await _client.DeleteAsync(created, ct);
        await WaitUntilAsync(
            async () => await _client.GetAsync<V1OperatorIntegrationTestEntity>(created.Metadata.Name, _ns.Namespace, ct) is null,
            ct);

        // The entity-global finalizer must have run exactly once and never concurrently, even though two
        // controllers matched the object.
        _tracker.Runs.Should().Be(1);
        _tracker.MaxConcurrent.Should().Be(1);
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await _ns.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _ns.DisposeAsync();
        _client.Dispose();
    }

    protected override void ConfigureHost(HostApplicationBuilder builder)
    {
        builder.Services
            .AddSingleton(_mock)
            .AddSingleton(_tracker)
            .AddKubernetesOperator(s => s.WithNamespace(_ns.Namespace))
            .AddControllerWithLabelSelector<ManagedController, V1OperatorIntegrationTestEntity, ManagedLabelSelector>()
            .AddControllerWithLabelSelector<ConfigController, V1OperatorIntegrationTestEntity, ConfigLabelSelector>()
            .AddFinalizer<TrackedFinalizer, V1OperatorIntegrationTestEntity>(FinalizerId);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new TimeoutException("Condition was not met within the timeout.");
    }

    private sealed class ManagedLabelSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("is-managed=true");
    }

    private sealed class ConfigLabelSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("is-managed=true,autogen-config=true");
    }

    private sealed class ManagedController(InvocationCounter<V1OperatorIntegrationTestEntity> svc)
        : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity, nameof(ManagedController));
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }

    private sealed class ConfigController(InvocationCounter<V1OperatorIntegrationTestEntity> svc)
        : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity, nameof(ConfigController));
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }

    private sealed class TrackedFinalizer(SideEffectTracker tracker) : IEntityFinalizer<V1OperatorIntegrationTestEntity>
    {
        public async Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> FinalizeAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            await tracker.RunAsync(cancellationToken);
            return ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity);
        }
    }
}

/// <summary>
/// Records how often a tracked operation ran and the maximum number of concurrent executions observed.
/// </summary>
public sealed class SideEffectTracker
{
    private int _current;
    private int _runs;
    private int _maxConcurrent;

    public int Runs => Volatile.Read(ref _runs);

    public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref _current);
        Interlocked.Increment(ref _runs);

        int observed;
        while (current > (observed = Volatile.Read(ref _maxConcurrent)) &&
               Interlocked.CompareExchange(ref _maxConcurrent, current, observed) != observed)
        {
            // retry until the max is at least the currently observed concurrency
        }

        try
        {
            await Task.Delay(500, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _current);
        }
    }
}
