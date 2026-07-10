// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Controller;

/// <summary>
/// Verifies that under <see cref="WatchStrategy.SharedPerEntity"/> a label change is delivered as a
/// selector exit (<c>DeletedAsync</c>) to the controller the object leaves and as an entry
/// (<c>ReconcileAsync</c>) to the controller it enters — with parity across both reconcile strategies,
/// including the default <see cref="ReconcileStrategy.ByGeneration"/> where a label-only change does not
/// bump <c>generation</c>.
/// </summary>
public abstract class MultipleControllersTransitionIntegrationTestBase : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    protected abstract ReconcileStrategy Strategy { get; }

    protected virtual LeaderElectionType LeaderElection => LeaderElectionType.None;

    [Fact]
    public async Task Should_Deliver_Exit_And_Entry_When_An_Object_Changes_Selector()
    {
        // 1. Create an object matching only the "a" controller; it reconciles it.
        var entity = new V1OperatorIntegrationTestEntity("transition", "username", _ns.Namespace);
        entity.Metadata.Labels = new Dictionary<string, string> { ["slice"] = "a" };

        var created = await _client.CreateAsync(entity, TestContext.Current.CancellationToken);
        await _mock.WaitForInvocations;
        _mock.Invocations.Select(i => i.Method).Should().ContainSingle().Which.Should().Be(nameof(SliceAController));

        // 2. Relabel it to match only "b". Even though the generation is unchanged, "a" must receive a
        //    Deleted (selector exit) and "b" a reconcile (selector entry).
        _mock.Clear();
        _mock.TargetInvocationCount = 2;
        created.Metadata.Labels!["slice"] = "b";
        await _client.UpdateAsync(created, TestContext.Current.CancellationToken);
        await _mock.WaitForInvocations;

        _mock.Invocations.Select(i => i.Method).Should()
            .BeEquivalentTo($"{nameof(SliceAController)}.Deleted", nameof(SliceBController));
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
            .AddKubernetesOperator(s => s
                .WithNamespace(_ns.Namespace)
                .WithWatchStrategy(WatchStrategy.SharedPerEntity)
                .WithReconcileStrategy(Strategy)
                .WithLeaderElection(LeaderElection))
            .AddControllerWithLabelSelector<SliceAController, V1OperatorIntegrationTestEntity, SliceALabelSelector>()
            .AddControllerWithLabelSelector<SliceBController, V1OperatorIntegrationTestEntity, SliceBLabelSelector>();
    }

    private sealed class SliceALabelSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("slice=a");
    }

    private sealed class SliceBLabelSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("slice=b");
    }

    private sealed class SliceAController(InvocationCounter<V1OperatorIntegrationTestEntity> svc)
        : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity, nameof(SliceAController));
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity, $"{nameof(SliceAController)}.Deleted");
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }
    }

    private sealed class SliceBController(InvocationCounter<V1OperatorIntegrationTestEntity> svc)
        : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity, nameof(SliceBController));
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity, $"{nameof(SliceBController)}.Deleted");
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }
    }
}

/// <summary>Selector transition parity under the default <see cref="ReconcileStrategy.ByGeneration"/>.</summary>
public sealed class MultipleControllersTransitionByGenerationIntegrationTest : MultipleControllersTransitionIntegrationTestBase
{
    protected override ReconcileStrategy Strategy => ReconcileStrategy.ByGeneration;
}

/// <summary>Selector transition parity under <see cref="ReconcileStrategy.ByResourceVersion"/>.</summary>
public sealed class MultipleControllersTransitionByResourceVersionIntegrationTest : MultipleControllersTransitionIntegrationTestBase
{
    protected override ReconcileStrategy Strategy => ReconcileStrategy.ByResourceVersion;
}

/// <summary>Selector transition parity on the leader-aware shared watcher (<see cref="LeaderElectionType.Single"/>).</summary>
public sealed class MultipleControllersTransitionLeaderSingleIntegrationTest : MultipleControllersTransitionIntegrationTestBase
{
    protected override ReconcileStrategy Strategy => ReconcileStrategy.ByGeneration;

    protected override LeaderElectionType LeaderElection => LeaderElectionType.Single;
}
