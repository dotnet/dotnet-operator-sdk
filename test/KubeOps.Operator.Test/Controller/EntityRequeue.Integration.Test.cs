// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Controller;

public sealed class EntityRequeueIntegrationTest : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task Should_Not_Queue_If_Not_Requested()
    {
        await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace),
            TestContext.Current.CancellationToken);
        await _mock.WaitForInvocations;

        _mock.Invocations.Count.Should().Be(1);
    }

    [Fact]
    public async Task Should_Requeue_Entity_And_Reconcile()
    {
        _mock.TargetInvocationCount = 5;
        await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace),
            TestContext.Current.CancellationToken);
        await _mock.WaitForInvocations;

        _mock.Invocations.Count.Should().Be(5);
    }

    [Fact]
    public async Task Should_Separately_And_Reliably_Requeue_And_Reconcile_Multiple_Entities_In_Parallel()
    {
        _mock.TargetInvocationCount = 100;
        await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity1", "username", _ns.Namespace),
            TestContext.Current.CancellationToken);
        await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity2", "username", _ns.Namespace),
            TestContext.Current.CancellationToken);
        await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity3", "username", _ns.Namespace),
            TestContext.Current.CancellationToken);
        await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity4", "username", _ns.Namespace),
            TestContext.Current.CancellationToken);
        await _mock.WaitForInvocations;

        // Expecting invocations, but since in parallel, there is a possibility to for target hit while other are in flight.
        _mock.Invocations.Count.Should().BeGreaterThanOrEqualTo(100).And.BeLessThan(105);
        var invocationsGroupedById = _mock.Invocations.GroupBy(item => item.Entity.Metadata.Uid).ToList();
        invocationsGroupedById.Count.Should().Be(4);
        var invocationDistributions = invocationsGroupedById
            .Select(g => (double)g.Count() / _mock.Invocations.Count * 100)
            .ToList();
        invocationDistributions
            .All(p => p is >= 15 and <= 35) // Check that invocations are reasonably distributed
            .Should()
            .BeTrue($"each entity invocation proportion should be within the specified range of total invocations, " +
                    $"but instead the distributions were: '{string.Join(", ", invocationDistributions)}'");
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
            .AddKubernetesOperator(s => s.Namespace = _ns.Namespace)
            .AddController<TestController, V1OperatorIntegrationTestEntity>();
    }

    private class TestController(InvocationCounter<V1OperatorIntegrationTestEntity> svc,
            EntityRequeue<V1OperatorIntegrationTestEntity> requeue)
        : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity);
            if (svc.Invocations.Count <= svc.TargetInvocationCount)
            {
                requeue(entity, RequeueType.Modified, TimeSpan.FromMilliseconds(1), CancellationToken.None);
            }

            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }
}
