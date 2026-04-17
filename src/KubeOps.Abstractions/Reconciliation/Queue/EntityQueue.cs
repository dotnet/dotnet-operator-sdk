// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation.Queue;

/// <summary>
/// Defines a delegate that queues a Kubernetes entity for reconciliation after a specified delay.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes entity being queued. Must implement <see cref="IKubernetesObject{V1ObjectMeta}"/>.
/// </typeparam>
/// <param name="entity">
/// The Kubernetes entity instance to be queued for reconciliation.
/// </param>
/// <param name="type">
/// One of the enumeration values that specifies the type of reconciliation operation to perform
/// (Added, Modified, or Deleted).
/// </param>
/// <param name="reconciliationTriggerSource">
/// One of the enumeration values that specifies whether the reconciliation was triggered by
/// the Kubernetes API server or by an internal operator operation.
/// </param>
/// <param name="queueIn">
/// The time span to wait before the entity is processed by the reconciler.
/// Use <see cref="TimeSpan.Zero"/> for immediate processing.
/// </param>
/// <param name="retryCount">
/// The number of times this entity has already been retried for the current reconciliation attempt.
/// Pass <c>0</c> for an initial (non-retry) enqueue. The value is carried forward across re-enqueues
/// so that retry-limit checks and back-off calculations can be applied consistently.
/// </param>
/// <param name="cancellationToken">
/// A token to monitor for cancellation requests while waiting for the queue duration to elapse.
/// </param>
/// <remarks>
/// <para>
/// This delegate is injected into controllers and other components via dependency injection to enable
/// scheduling entities for delayed or immediate reconciliation. The delegate is typically created by
/// <see cref="IEntityQueueFactory"/> and wraps the internal queue implementation.
/// </para>
/// <para>
/// The <paramref name="type"/> parameter determines which controller methods are invoked
/// (e.g., <c>ReconcileAsync</c> for Added/Modified, or finalizer logic for Deleted).
/// The <paramref name="reconciliationTriggerSource"/> provides diagnostic context about whether
/// the request originated from Kubernetes watch events or from internal operations such as
/// error retries, conflict resolution, or periodic requeues.
/// </para>
/// <para>
/// Common usage scenarios include:
/// <list type="bullet">
/// <item><description>Immediate reconciliation after a Kubernetes watch event (API server source, zero delay)</description></item>
/// <item><description>Delayed retry after a reconciliation error (operator source, configured delay)</description></item>
/// <item><description>Delayed retry after a UID conflict (operator source, configured delay)</description></item>
/// <item><description>Periodic reconciliation check (operator source, configured interval)</description></item>
/// </list>
/// </para>
/// </remarks>
/// <seealso cref="ReconciliationType"/>
/// <seealso cref="ReconciliationTriggerSource"/>
/// <seealso cref="IEntityQueueFactory"/>
public delegate void EntityQueue<in TEntity>(
    TEntity entity, ReconciliationType type, ReconciliationTriggerSource reconciliationTriggerSource, TimeSpan queueIn, int retryCount, CancellationToken cancellationToken)
    where TEntity : IKubernetesObject<V1ObjectMeta>;
