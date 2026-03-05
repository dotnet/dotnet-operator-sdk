// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Defines a timed queue for scheduling Kubernetes entities for delayed reconciliation.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes entity. Must implement <see cref="IKubernetesObject{V1ObjectMeta}"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// This interface provides a priority queue implementation that schedules entities for processing
/// after a configurable delay. Entities can be enqueued with a <see cref="TimeSpan"/> that determines
/// when they become eligible for reconciliation. The queue also supports removal of scheduled entities
/// and implements <see cref="IAsyncEnumerable{T}"/> for consuming ready entries.
/// </para>
/// <para>
/// The queue is typically used by the operator framework to manage reconciliation timing for both
/// immediate processing (via <see cref="TimeSpan.Zero"/>) and delayed retries. Each queued entry
/// includes the entity, the reconciliation type, and the trigger source for diagnostic purposes.
/// </para>
/// </remarks>
/// <seealso cref="QueueEntry{TEntity}"/>
/// <seealso cref="ReconciliationType"/>
/// <seealso cref="ReconciliationTriggerSource"/>
public interface ITimedEntityQueue<TEntity> : IDisposable, IAsyncEnumerable<QueueEntry<TEntity>>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// Enqueues the specified entity for reconciliation after the specified delay.
    /// </summary>
    /// <param name="entity">The Kubernetes entity to be queued for reconciliation.</param>
    /// <param name="type">
    /// One of the enumeration values that specifies the type of reconciliation operation to perform.
    /// </param>
    /// <param name="reconciliationTriggerSource">
    /// One of the enumeration values that specifies whether the reconciliation was triggered by
    /// the Kubernetes API server or by an internal operator operation.
    /// </param>
    /// <param name="queueIn">
    /// The time span to wait before the entity becomes eligible for reconciliation.
    /// Use <see cref="TimeSpan.Zero"/> for immediate processing.
    /// </param>
    /// <param name="retryCount">
    /// The number of previous failed reconciliation attempts for this entry. Pass <c>0</c> for the
    /// initial (non-retry) attempt. The value is preserved in the resulting <see cref="QueueEntry{TEntity}"/>
    /// so that exponential back-off and retry-limit checks remain correct across requeues.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests during the asynchronous operation.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous enqueue operation.
    /// </returns>
    /// <remarks>
    /// If an entity with the same key is already queued, the entry scheduled to run earliest is kept.
    /// </remarks>
    Task Enqueue(TEntity entity, ReconciliationType type, ReconciliationTriggerSource reconciliationTriggerSource, TimeSpan queueIn, int retryCount, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the specified entity from the queue if it is currently scheduled.
    /// </summary>
    /// <param name="entity">The Kubernetes entity to be removed from the queue.</param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests during the asynchronous operation.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous remove operation.
    /// </returns>
    /// <remarks>
    /// If the entity is not found in the queue, this method completes successfully without error.
    /// This method is typically called when an entity is deleted or when a scheduled operation
    /// is no longer needed.
    /// </remarks>
    Task Remove(TEntity entity, CancellationToken cancellationToken);
}
