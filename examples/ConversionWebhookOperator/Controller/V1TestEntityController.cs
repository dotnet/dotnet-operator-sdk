// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using ConversionWebhookOperator.Entities;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;

namespace ConversionWebhookOperator.Controller;

[EntityRbac(typeof(V1TestEntity), Verbs = RbacVerb.All)]
public class V1TestEntityController(ILogger<V1TestEntityController> logger) : IEntityController<V1TestEntity>
{
    public Task<Result<V1TestEntity>> ReconcileAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reconciling entity {Entity}.", entity);
        return Task.FromResult(Result<V1TestEntity>.ForSuccess(entity));
    }

    public Task<Result<V1TestEntity>> DeletedAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleted entity {Entity}.", entity);
        return Task.FromResult(Result<V1TestEntity>.ForSuccess(entity));
    }
}
