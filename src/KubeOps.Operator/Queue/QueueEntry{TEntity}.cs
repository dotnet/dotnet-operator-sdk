// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Represents a single entry in the reconciliation queue, pairing a Kubernetes entity with its
/// reconciliation context.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes entity associated with this queue entry.
/// </typeparam>
/// <param name="Entity">The Kubernetes entity to be reconciled.</param>
/// <param name="ReconciliationType">
/// One of the enumeration values that specifies the reconciliation operation to perform.
/// </param>
/// <param name="ReconciliationTriggerSource">
/// One of the enumeration values that specifies the origin of the reconciliation request.
/// </param>
/// <param name="RetryCount">
/// The number of previous failed reconciliation attempts for this entry.
/// A value of <c>0</c> indicates the first (non-retry) attempt.
/// </param>
/// <remarks>
/// Entries originate either from Kubernetes watch events (API server source) or from internal
/// operator operations such as error retries, conflict retries, or periodic requeues (operator source).
/// The combination of <see cref="ReconciliationType"/> and <see cref="ReconciliationTriggerSource"/>
/// gives the reconciler full context to decide how to process the entry.
/// When <see cref="RetryCount"/> is greater than zero, the entry is being retried after a previous failure.
/// The operator uses this value to apply exponential back-off and enforce the configured retry limit.
/// </remarks>
public readonly record struct QueueEntry<TEntity>(
    TEntity Entity,
    ReconciliationType ReconciliationType,
    ReconciliationTriggerSource ReconciliationTriggerSource,
    int RetryCount);
