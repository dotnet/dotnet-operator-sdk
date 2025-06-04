using KubeOps.Abstractions.Controller;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.HostedServices;

public sealed class LeaderAwareHostedServiceDisposeIntegrationTest : HostedServiceDisposeIntegrationTest
{
    protected override void ConfigureHost(HostApplicationBuilder builder)
    {
        builder.Services
            .AddKubernetesOperator(op => op.EnableLeaderElection = true)
            .AddController<TestController, V1OperatorIntegrationTestEntity>();
    }

    private sealed class TestController : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<Result<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<V1OperatorIntegrationTestEntity>.ForSuccess(entity));

        public Task<Result<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<V1OperatorIntegrationTestEntity>.ForSuccess(entity));
    }
}
