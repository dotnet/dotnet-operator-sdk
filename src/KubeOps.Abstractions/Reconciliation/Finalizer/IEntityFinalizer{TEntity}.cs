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
    /// Returns <c>true</c> when this finalizer is responsible for the given entity.
    /// The default implementation returns <c>true</c>, preserving backward-compatible behaviour.
    ///
    /// When <see cref="KubeOps.Abstractions.Builder.OperatorSettings.AutoAttachFinalizers"/> is enabled,
    /// only finalizers that return <c>true</c> from <see cref="ShouldHandle"/> are attached to the entity.
    /// Once attached, the finalizer is dispatched by its identifier as usual — <see cref="ShouldHandle"/>
    /// acts as a one-time responsibility claim at attach time, not an ongoing gate.
    /// </summary>
    /// <param name="entity">The entity the reconciler is considering for this finalizer.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValueTask{Boolean}"/> that resolves to <c>true</c> if this finalizer should claim the entity.</returns>
    ValueTask<bool> ShouldHandle(TEntity entity, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

    /// <summary>
    /// Finalize an entity that is pending for deletion.
    /// </summary>
    /// <param name="entity">The kubernetes entity that needs to be finalized.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result of the reconcile process.</returns>
    Task<ReconciliationResult<TEntity>> FinalizeAsync(TEntity entity, CancellationToken cancellationToken);
}
