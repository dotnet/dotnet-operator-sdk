// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Controller;

public sealed class DeletedEntityRequeueIntegrationTest : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task Should_Cancel_Requeue_If_Entity_Is_Deleted()
    {
        _mock.TargetInvocationCount = 2;
        var e = await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace));
        await _client.DeleteAsync(e);
        await _mock.WaitForInvocations;

        _mock.Invocations.Count.Should().Be(2);
        var timedEntityQueue = Services.GetRequiredService<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        timedEntityQueue.Should().NotBeNull();
        timedEntityQueue.Should().BeOfType<TimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        timedEntityQueue.As<TimedEntityQueue<V1OperatorIntegrationTestEntity>>().Count.Should().Be(0);
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await _ns.InitializeAsync();
    }

    public override async Task DisposeAsync()
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
            requeue(entity, RequeueType.Modified, TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity);
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }
    }
}
