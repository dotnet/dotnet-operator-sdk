// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.HostedServices;

public sealed class LeaderAwareHostedServiceDisposeIntegrationTest : HostedServiceDisposeIntegrationTest
{
    protected override void ConfigureHost(HostApplicationBuilder builder)
    {
        builder.Services
            .AddKubernetesOperator(op => op.LeaderElectionType = LeaderElectionType.Single)
            .AddController<TestController, V1OperatorIntegrationTestEntity>();
    }

    private sealed class TestController : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }
}
