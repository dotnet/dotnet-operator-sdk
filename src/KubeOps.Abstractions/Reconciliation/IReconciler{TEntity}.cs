// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation;

/// <summary>
/// Defines methods for handling reconciliation processes related to Kubernetes resources.
/// This interface provides the necessary functionality for handling the lifecycle events
/// of a resource, such as creation, modification, and deletion.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes resource, which must implement <see cref="IKubernetesObject{V1ObjectMeta}"/>.
/// </typeparam>
public interface IReconciler<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// Handles the reconciliation process when a new entity is created.
    /// </summary>
    /// <param name="entity">The entity to reconcile during its creation.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, with a result of the reconciliation process.</returns>
    Task<ReconciliationResult<TEntity>> ReconcileCreation(TEntity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Handles the reconciliation process when an existing entity is modified.
    /// </summary>
    /// <param name="entity">The entity to reconcile after modification.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, with a result of the reconciliation process.</returns>
    Task<ReconciliationResult<TEntity>> ReconcileModification(TEntity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Handles the reconciliation process when an entity is deleted.
    /// </summary>
    /// <param name="entity">The entity to reconcile during its deletion.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, with a result of the reconciliation process.</returns>
    Task<ReconciliationResult<TEntity>> ReconcileDeletion(TEntity entity, CancellationToken cancellationToken);
}
