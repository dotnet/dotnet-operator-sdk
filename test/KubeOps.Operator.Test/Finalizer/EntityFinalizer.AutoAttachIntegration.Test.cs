// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Finalizer;

[Trait(TestTrait.Area, TestArea.Finalizers)]
public sealed class EntityFinalizerAutoAttachIntegrationTest : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task Should_Attach_Finalizer_On_Entity_And_Call_Reconcile_Once()
    {
        var created = await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("first", "first", _ns.Namespace, couldChangeStatus: true),
            TestContext.Current.CancellationToken);

        // Watch from the resource version of the created entity: the API server then replays every later
        // event, no matter when the watch connection is established. Without it, the operator could attach
        // the finalizer before the watch is open, and the watch would miss events (#1305).
        // 2 events: finalizer attached, status updated.
        var watcherCounter = new InvocationCounter<V1OperatorIntegrationTestEntity> { TargetInvocationCount = 2 };
        using var watcher =
            _client.Watch<V1OperatorIntegrationTestEntity>(
                (_, e) => watcherCounter.Invocation(e),
                @namespace: _ns.Namespace,
                resourceVersion: created.ResourceVersion(),
                cancellationToken: TestContext.Current.CancellationToken);

        await _mock.WaitForInvocations;
        await watcherCounter.WaitForInvocations;

        var result = await _client.GetAsync<V1OperatorIntegrationTestEntity>(
            "first",
            _ns.Namespace,
            TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Metadata.Finalizers.Should().Contain("operator.test/testfinalizer");
        result.Status.Status.Should().Be("reconciled");
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await _ns.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        var entities = await _client.ListAsync<V1OperatorIntegrationTestEntity>(_ns.Namespace);
        foreach (var e in entities)
        {
            if (e.Metadata.Finalizers is null)
            {
                continue;
            }

            e.Metadata.Finalizers.Clear();
            await _client.UpdateAsync(e);
        }

        await _ns.DisposeAsync();
        _client.Dispose();
    }

    protected override void ConfigureHost(HostApplicationBuilder builder)
    {
        builder.Services
            .AddSingleton(_mock)
            .AddKubernetesOperator(s => { s.Namespace = _ns.Namespace; })
            .AddController<TestController, V1OperatorIntegrationTestEntity>()
            .AddFinalizer<TestFinalizer, V1OperatorIntegrationTestEntity>("operator.test/testfinalizer");
    }

    private class TestController(
        InvocationCounter<V1OperatorIntegrationTestEntity> svc,
        IKubernetesClient client)
        : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public async Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            if (entity.Spec.CouldChangeStatus)
            {
                // status change: issue 1001 (https://github.com/dotnet/dotnet-operator-sdk/issues/1001)
                entity.Status.Status = "reconciled";
                entity = await client.UpdateStatusAsync(entity, cancellationToken);
            }

            svc.Invocation(entity);

            return ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity);
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity);
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }
    }

    private class TestFinalizer(
        InvocationCounter<V1OperatorIntegrationTestEntity> svc)
        : IEntityFinalizer<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> FinalizeAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity);
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }
    }
}
