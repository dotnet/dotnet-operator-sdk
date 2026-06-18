// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;

using OtelOperator.Entities;

namespace OtelOperator.Controller;

[EntityRbac(typeof(V1OtelDemoEntity), Verbs = RbacVerb.All)]
public sealed class V1OtelDemoEntityController(ILogger<V1OtelDemoEntityController> logger)
    : IEntityController<V1OtelDemoEntity>
{
    public Task<ReconciliationResult<V1OtelDemoEntity>> ReconcileAsync(
        V1OtelDemoEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reconciling entity {Namespace}/{Name}.", entity.Namespace(), entity.Name());
        return Task.FromResult(ReconciliationResult<V1OtelDemoEntity>.Success(entity));
    }

    public Task<ReconciliationResult<V1OtelDemoEntity>> DeletedAsync(
        V1OtelDemoEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleted entity {Namespace}/{Name}.", entity.Namespace(), entity.Name());
        return Task.FromResult(ReconciliationResult<V1OtelDemoEntity>.Success(entity));
    }
}
