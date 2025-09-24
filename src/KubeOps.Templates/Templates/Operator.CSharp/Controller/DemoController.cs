using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Rbac;

using Microsoft.Extensions.Logging;

using GeneratedOperatorProject.Entities;

namespace GeneratedOperatorProject.Controller;

[EntityRbac(typeof(V1DemoEntity), Verbs = RbacVerb.All)]
public class DemoController(ILogger<DemoController> logger) : IEntityController<V1DemoEntity>
{
    public Task ReconcileAsync(V1DemoEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reconcile entity {MetadataName}", entity.Metadata.Name);

        return Task.CompletedTask;
    }

    public Task DeletedAsync(V1DemoEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleted entity {Entity}.", entity);

        return Task.CompletedTask;
    }
}
