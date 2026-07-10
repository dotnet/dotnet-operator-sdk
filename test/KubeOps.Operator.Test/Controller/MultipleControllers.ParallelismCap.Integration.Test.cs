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
/// Regression for the parallelism-multiplication major: <c>MaxParallelReconciliations</c> is a single
/// budget per entity type, shared by all of that entity's controllers. With three controllers and a limit
/// of one, at most one reconciliation may run at a time — not one per controller.
/// </summary>
public sealed class MultipleControllersParallelismCapIntegrationTest : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new() { TargetInvocationCount = 3 };
    private readonly SideEffectTracker _tracker = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task Should_Share_A_Single_Parallelism_Budget_Across_Controllers()
    {
        var ct = TestContext.Current.CancellationToken;

        // Three objects, each matching exactly one of the three controllers. All are reconciled, but the
        // shared budget of one permits only a single reconciliation at a time.
        for (var i = 1; i <= 3; i++)
        {
            var entity = new V1OperatorIntegrationTestEntity($"object-{i}", "username", _ns.Namespace);
            entity.Metadata.Labels = new Dictionary<string, string> { ["controller"] = i.ToString() };
            await _client.CreateAsync(entity, ct);
        }

        await _mock.WaitForInvocations;

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
            .AddKubernetesOperator(s => s
                .WithNamespace(_ns.Namespace)
                .WithParallelReconciliation(p => p.WithMaxParallelReconciliations(1)))
            .AddControllerWithLabelSelector<OneController, V1OperatorIntegrationTestEntity, OneSelector>()
            .AddControllerWithLabelSelector<TwoController, V1OperatorIntegrationTestEntity, TwoSelector>()
            .AddControllerWithLabelSelector<ThreeController, V1OperatorIntegrationTestEntity, ThreeSelector>();
    }

    private sealed class OneSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("controller=1");
    }

    private sealed class TwoSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("controller=2");
    }

    private sealed class ThreeSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("controller=3");
    }

    private abstract class TrackingController(
        InvocationCounter<V1OperatorIntegrationTestEntity> svc,
        SideEffectTracker tracker) : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public async Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            await tracker.RunAsync(cancellationToken);
            svc.Invocation(entity, GetType().Name);
            return ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity);
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }

    private sealed class OneController(
        InvocationCounter<V1OperatorIntegrationTestEntity> svc, SideEffectTracker tracker)
        : TrackingController(svc, tracker);

    private sealed class TwoController(
        InvocationCounter<V1OperatorIntegrationTestEntity> svc, SideEffectTracker tracker)
        : TrackingController(svc, tracker);

    private sealed class ThreeController(
        InvocationCounter<V1OperatorIntegrationTestEntity> svc, SideEffectTracker tracker)
        : TrackingController(svc, tracker);
}
