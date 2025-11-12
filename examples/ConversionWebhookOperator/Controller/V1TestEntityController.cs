// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using ConversionWebhookOperator.Entities;

using k8s.Models;

using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;

namespace ConversionWebhookOperator.Controller;

[EntityRbac(typeof(V1TestEntity), Verbs = RbacVerb.All)]
public sealed class V1TestEntityController(ILogger<V1TestEntityController> logger) : IEntityController<V1TestEntity>
{
    public Task<ReconciliationResult<V1TestEntity>> ReconcileAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reconciling entity {Namespace}/{Name}.", entity.Namespace(), entity.Name());
        return Task.FromResult(ReconciliationResult<V1TestEntity>.Success(entity));
    }

    public Task<ReconciliationResult<V1TestEntity>> DeletedAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleted entity {Namespace}/{Name}.", entity.Namespace(), entity.Name());
        return Task.FromResult(ReconciliationResult<V1TestEntity>.Success(entity));
    }
}
