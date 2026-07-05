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
/// The multi-controller scenario of issue #909 under <see cref="WatchStrategy.SharedPerEntity"/>: a
/// single shared watch connection dispatches events to all controllers whose label selectors match,
/// evaluated client-side.
/// </summary>
public sealed class MultipleControllersSharedWatcherIntegrationTest : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task Should_Dispatch_Shared_Watch_Events_To_All_Matching_Controllers()
    {
        _mock.TargetInvocationCount = 2;

        var entity = new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace);
        entity.Metadata.Labels = new Dictionary<string, string>
        {
            ["is-managed"] = "true",
            ["autogen-config"] = "true",
        };

        await _client.CreateAsync(entity, TestContext.Current.CancellationToken);
        await _mock.WaitForInvocations;

        _mock.Invocations.Select(i => i.Method).Should()
            .BeEquivalentTo(nameof(ManagedController), nameof(ConfigController));
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
                .WithWatchStrategy(WatchStrategy.SharedPerEntity))
            .AddControllerWithLabelSelector<ManagedController, V1OperatorIntegrationTestEntity, ManagedLabelSelector>()
            .AddControllerWithLabelSelector<ConfigController, V1OperatorIntegrationTestEntity, ConfigLabelSelector>();
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
}
