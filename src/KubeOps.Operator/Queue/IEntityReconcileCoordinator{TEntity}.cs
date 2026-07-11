// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Coordinates reconciliation concurrency for a single entity type across every controller pipeline
/// (queue consumer) of that type. When multiple controllers are registered for the same entity, each
/// pipeline runs its own consumer; without a shared coordinator each consumer would enforce the
/// parallelism limit and the per-entity mutual exclusion on its own, so the effective limit would be
/// multiplied by the controller count and the same object could be reconciled (and finalized)
/// concurrently by different pipelines. A single coordinator instance per entity type restores both
/// guarantees: one global parallelism budget and one exclusive lock per entity UID.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type this coordinator serializes.</typeparam>
public interface IEntityReconcileCoordinator<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// Acquires one slot of the global parallelism budget
    /// (<see cref="ParallelReconciliationSettings.MaxParallelReconciliations"/>), shared by all pipelines
    /// of this entity type. Acquire before dequeuing to apply back-pressure; release with
    /// <see cref="ReleaseParallelSlot"/> once processing completes.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when a slot has been acquired.</returns>
    Task AcquireParallelSlotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Releases a slot previously acquired with <see cref="AcquireParallelSlotAsync"/>.
    /// </summary>
    void ReleaseParallelSlot();

    /// <summary>
    /// Attempts to acquire the exclusive per-UID lock for an entity, honoring the configured
    /// <see cref="ParallelReconciliationConflictStrategy"/>, so the same object is never reconciled
    /// concurrently — even by different pipelines of the same entity type.
    /// </summary>
    /// <param name="entity">The entity whose UID identifies the exclusive lock.</param>
    /// <param name="strategy">The conflict strategy deciding whether to wait, or fail fast, on contention.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A lease that releases the lock when disposed, or <see langword="null"/> when the entity is already
    /// being reconciled and the strategy is non-blocking
    /// (<see cref="ParallelReconciliationConflictStrategy.Discard"/> /
    /// <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>). If the wait is cancelled,
    /// no lock is held and internal bookkeeping is rolled back before the cancellation surfaces.
    /// </returns>
    Task<IAsyncDisposable?> AcquireEntityLockAsync(
        TEntity entity,
        ParallelReconciliationConflictStrategy strategy,
        CancellationToken cancellationToken);
}
