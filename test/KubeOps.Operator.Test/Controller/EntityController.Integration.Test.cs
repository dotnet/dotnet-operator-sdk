using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Controller;

public class EntityControllerIntegrationTest : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task Should_Call_Reconcile_On_New_Entity()
    {
        await _client.CreateAsync(new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace));
        await _mock.WaitForInvocations;

        _mock.Invocations.Count.Should().Be(1);
        var (method, entity) = _mock.Invocations[0];
        method.Should().Be("ReconcileAsync");
        entity.Should().BeOfType<V1OperatorIntegrationTestEntity>();
        entity.Name().Should().Be("test-entity");
        entity.Spec.Username.Should().Be("username");
    }

    [Fact]
    public async Task Should_Call_Reconcile_On_Modification_Of_Entity()
    {
        _mock.TargetInvocationCount = 2;

        var result =
            await _client.CreateAsync(new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace));
        result.Spec.Username = "changed";
        await _client.UpdateAsync(result);
        await _mock.WaitForInvocations;

        _mock.Invocations.Count.Should().Be(2);

        Check(0, "username");
        Check(1, "changed");
        return;

        void Check(int idx, string username)
        {
            var (method, entity) = _mock.Invocations[idx];
            method.Should().Be("ReconcileAsync");
            entity.Should().BeOfType<V1OperatorIntegrationTestEntity>();
            entity.Spec.Username.Should().Be(username);
        }
    }

    [Fact]
    public async Task Should_Not_Call_Reconcile_When_Only_Entity_Status_Changed()
    {
        _mock.TargetInvocationCount = 1;

        var result =
            await _client.CreateAsync(new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace));
        result.Status.Status = "changed";
        // Update or UpdateStatus do not call Reconcile
        await _client.UpdateAsync(result);
        await _client.UpdateStatusAsync(result);
        await _mock.WaitForInvocations;

        _mock.Invocations.Count.Should().Be(1);

        (string method, V1OperatorIntegrationTestEntity entity) = _mock.Invocations.Single();
        method.Should().Be("ReconcileAsync");
        entity.Should().BeOfType<V1OperatorIntegrationTestEntity>();
        entity.Spec.Username.Should().Be("username");
    }

    [Fact]
    public async Task Should_Call_Delete_For_Deleted_Entity()
    {
        _mock.TargetInvocationCount = 2;

        var result =
            await _client.CreateAsync(new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace));
        await _client.DeleteAsync(result);
        await _mock.WaitForInvocations;

        _mock.Invocations.Count.Should().Be(2);
        _mock.Invocations[0].Method.Should().Be("ReconcileAsync");
        _mock.Invocations[1].Method.Should().Be("DeletedAsync");
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

    private class TestController(InvocationCounter<V1OperatorIntegrationTestEntity> svc) : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<Result<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity);
            return Task.FromResult(Result<V1OperatorIntegrationTestEntity>.ForSuccess(entity));
        }

        public Task<Result<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            svc.Invocation(entity);
            return Task.FromResult(Result<V1OperatorIntegrationTestEntity>.ForSuccess(entity));
        }
    }
}
