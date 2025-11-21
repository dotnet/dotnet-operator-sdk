// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation.Finalizer;

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
    Task<ReconciliationResult<TEntity>> FinalizeAsync(TEntity entity, CancellationToken cancellationToken);
}
