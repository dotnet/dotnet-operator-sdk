using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Events;
using KubeOps.Abstractions.Queue;
using KubeOps.Abstractions.Rbac;

using Microsoft.Extensions.Logging;

using Operator.Entities;

namespace Operator.Controller;

[EntityRbac(typeof(V1TestEntity), Verbs = RbacVerb.All)]
public sealed  class V1TestEntityController(ILogger<V1TestEntityController> logger)
    : IEntityController<V1TestEntity>
{
    public Task<Result<V1TestEntity>> ReconcileAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reconciling entity {Entity}.", entity);
        return Task.FromResult(Result<V1TestEntity>.ForSuccess(entity));
    }

    public Task<Result<V1TestEntity>> DeletedAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting entity {Entity}.", entity);
        return Task.FromResult(Result<V1TestEntity>.ForSuccess(entity));
    }
}
