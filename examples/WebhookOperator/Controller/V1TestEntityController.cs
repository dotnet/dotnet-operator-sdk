using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;

using WebhookOperator.Entities;

namespace WebhookOperator.Controller;

[EntityRbac(typeof(V1TestEntity), Verbs = RbacVerb.All)]
public sealed class V1TestEntityController(ILogger<V1TestEntityController> logger) : IEntityController<V1TestEntity>
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
