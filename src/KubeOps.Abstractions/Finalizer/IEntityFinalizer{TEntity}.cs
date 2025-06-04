using k8s;
using k8s.Models;

using KubeOps.Abstractions.Controller;

namespace KubeOps.Abstractions.Finalizer;

/// <summary>
/// Finalizer for an entity.
/// </summary>
/// <typeparam name="TEntity">The type of the entity.</typeparam>
public interface IEntityFinalizer<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// Finalize an entity that is pending for deletion.
    /// </summary>
    /// <param name="entity">The kubernetes entity that needs to be finalized.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result of the reconcile process.</returns>
    Task<Result<TEntity>> FinalizeAsync(TEntity entity, CancellationToken cancellationToken);
}
