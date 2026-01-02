// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test;

public sealed class NamespacedOperatorIntegrationTest : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();
    private V1Namespace _otherNamespace = null!;

    [Fact]
    public async Task Should_Call_Reconcile_On_Entity_In_Namespace()
    {
        var otherNsWatchCounter = new InvocationCounter<V1OperatorIntegrationTestEntity> { TargetInvocationCount = 1 };
        using var otherNsWatcher =
            _client.Watch<V1OperatorIntegrationTestEntity>((_, e) => otherNsWatchCounter.Invocation(e),
                @namespace: _otherNamespace.Name(),
                cancellationToken: TestContext.Current.CancellationToken);

        await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity", "username", _otherNamespace.Name()),
            TestContext.Current.CancellationToken);
        await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace),
            TestContext.Current.CancellationToken);
        await _mock.WaitForInvocations;
        await otherNsWatchCounter.WaitForInvocations;
        _mock.Invocations.Count.Should().Be(1);
        otherNsWatchCounter.Invocations.Count.Should().Be(1);
    }

    [Fact]
    public async Task Should_Not_Call_Reconcile_On_Entity_In_Other_Namespace()
    {
        var watcherCounter = new InvocationCounter<V1OperatorIntegrationTestEntity> { TargetInvocationCount = 1 };
        using var watcher = _client.Watch<V1OperatorIntegrationTestEntity>((_, e) => watcherCounter.Invocation(e),
            @namespace: _otherNamespace.Name(),
            cancellationToken: TestContext.Current.CancellationToken);

        await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity2", "username", _otherNamespace.Name()),
            TestContext.Current.CancellationToken);
        await watcherCounter.WaitForInvocations;
        _mock.Invocations.Count.Should().Be(0);
        watcherCounter.Invocations.Count.Should().Be(1);
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _otherNamespace =
            await _client.CreateAsync(
                new V1Namespace
                {
                    Metadata = new() { Name = Guid.NewGuid().ToString().ToLower() },
                }
                .Initialize());
        await _ns.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _client.DeleteAsync(_otherNamespace);
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

    private class TestController(InvocationCounter<V1OperatorIntegrationTestEntity> svc)
        : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity);
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity);
            return Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
        }
    }
}
