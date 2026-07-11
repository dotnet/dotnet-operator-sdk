// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Controller;

/// <summary>
/// Acceptance test for https://github.com/dotnet/dotnet-operator-sdk/issues/909: two controllers for the
/// same entity type with overlapping label selectors must both reconcile an entity that matches both
/// selectors. Each controller runs its own watch → queue → reconcile pipeline with an isolated
/// deduplication cache partition.
/// </summary>
public sealed class MultipleControllersIntegrationTest : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task Should_Reconcile_With_Both_Controllers_For_Overlapping_Selectors()
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

    [Fact]
    public async Task Should_Reconcile_Only_With_Matching_Controller()
    {
        var entity = new V1OperatorIntegrationTestEntity("test-entity-managed", "username", _ns.Namespace);
        entity.Metadata.Labels = new Dictionary<string, string>
        {
            ["is-managed"] = "true",
        };

        await _client.CreateAsync(entity, TestContext.Current.CancellationToken);
        await _mock.WaitForInvocations;

        // Give the (non-matching) second controller a moment to prove it stays silent.
        await Task.Delay(500, TestContext.Current.CancellationToken);

        _mock.Invocations.Select(i => i.Method).Should()
            .BeEquivalentTo(nameof(ManagedController));
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
