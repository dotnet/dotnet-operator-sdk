using k8s.Models;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Finalizer;

using Microsoft.Extensions.Logging;

using GeneratedOperatorProject.Entities;

namespace GeneratedOperatorProject.Finalizer;

public sealed class DemoFinalizer(ILogger<DemoFinalizer> logger) : IEntityFinalizer<V1DemoEntity>
{
    public Task<ReconciliationResult<V1DemoEntity>> FinalizeAsync(V1DemoEntity entity, CancellationToken cancellationToken)
    {
        logger.LogInformation($"entity {entity.Name()} called {nameof(FinalizeAsync)}.");

        return Task.FromResult(ReconciliationResult<V1DemoEntity>.Success(entity));
    }
}
