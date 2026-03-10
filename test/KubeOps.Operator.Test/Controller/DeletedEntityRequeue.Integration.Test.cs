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
    private readonly MethodInvocationObserver<V1OperatorIntegrationTestEntity> _observer = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact(Timeout = 30_000)]
    public async Task Should_Cancel_Requeue_If_Entity_Is_Deleted()
    {
        // Arrange
        var waitTask = _observer
            .WaitForMethod(
                nameof(IEntityController<>.DeletedAsync),
                TestContext.Current.CancellationToken);

        // Act
        var entity = await _client
            .CreateAsync(
                new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace),
                TestContext.Current.CancellationToken);

        await Task.Delay(200, TestContext.Current.CancellationToken);

        await _client
            .DeleteAsync(
                entity,
                TestContext.Current.CancellationToken);

        await waitTask;

        // Assert
        _observer.Invocations.Count.Should().Be(2);
        _observer.Invocations[0].Method.Should().Be(nameof(TestController.ReconcileAsync));
        _observer.Invocations[1].Method.Should().Be(nameof(TestController.DeletedAsync));

        var timedEntityQueue = Services.GetRequiredService<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        timedEntityQueue.Should().NotBeNull();
        timedEntityQueue.Should().BeOfType<TimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        timedEntityQueue.As<TimedEntityQueue<V1OperatorIntegrationTestEntity>>().Count.Should().Be(0);
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
            .AddSingleton(_observer)
            .AddKubernetesOperator(s => s.Namespace = _ns.Namespace)
            .AddController<TestController, V1OperatorIntegrationTestEntity>();
    }

    private sealed class TestController(MethodInvocationObserver<V1OperatorIntegrationTestEntity> observer,
            EntityQueue<V1OperatorIntegrationTestEntity> queue)
        : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            observer.RecordInvocation(entity);
            queue(entity, ReconciliationType.Modified, ReconciliationTriggerSource.Operator, TimeSpan.FromSeconds(60), retryCount: 0, TestContext.Current.CancellationToken);
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            observer.RecordInvocation(entity);
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }
    }
}
