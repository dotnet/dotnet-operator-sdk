using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Rbac;

using Microsoft.Extensions.Logging;

using GeneratedOperatorProject.Entities;

namespace GeneratedOperatorProject.Controller;

[EntityRbac(typeof(V1DemoEntity), Verbs = RbacVerb.All)]
public sealed class DemoController(ILogger<DemoController> logger) : IEntityController<V1DemoEntity>
{
    public Task<ReconciliationResult<V1DemoEntity>> ReconcileAsync(V1DemoEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reconcile entity {MetadataName}", entity.Metadata.Name);

        return Task.FromResult(ReconciliationResult<V1DemoEntity>.Success(entity));
    }

    public Task<ReconciliationResult<V1DemoEntity>> DeletedAsync(V1DemoEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleted entity {Entity}.", entity);

        return Task.FromResult(ReconciliationResult<V1DemoEntity>.Success(entity));
    }
}
