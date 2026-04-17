// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Defines strategies for handling concurrent reconciliation attempts for the same Kubernetes entity UID.
/// </summary>
/// <remarks>
/// <para>
/// When a Kubernetes entity is being reconciled and a new reconciliation request arrives for the same
/// entity (identified by its UID), the operator uses this strategy to determine how to handle the conflict.
/// This prevents race conditions where multiple reconciliation loops might modify the same entity simultaneously.
/// </para>
/// <para>
/// The choice of strategy affects both the consistency guarantees and the performance characteristics
/// of the operator. Choose the strategy based on your specific requirements for idempotency, update
/// frequency, and acceptable latency.
/// </para>
/// </remarks>
/// <seealso cref="ParallelReconciliationOptions"/>
public enum ParallelReconciliationConflictStrategy
{
    /// <summary>
    /// Discards the reconciliation request if the entity is currently being reconciled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This strategy assumes that the currently executing reconciliation will observe the latest state
    /// of the entity, making additional reconciliation requests redundant. This is the most performant
    /// option as it avoids queuing overhead and reduces system load.
    /// </para>
    /// <para>
    /// Use this strategy when reconciliation operations are idempotent and read the latest entity state
    /// from the Kubernetes API server during execution. This is suitable for most reconciliation scenarios
    /// where the reconciler always fetches fresh data.
    /// </para>
    /// </remarks>
    Discard,

    /// <summary>
    /// Requeues the entity for reconciliation after a configured delay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This strategy ensures that no reconciliation requests are lost by placing the entity back into
    /// the reconciliation queue with a time delay. The delay is configured via
    /// <see cref="ParallelReconciliationOptions.RequeueDelay"/>. This approach balances between
    /// ensuring all state changes are eventually processed while avoiding immediate re-conflicts.
    /// </para>
    /// <para>
    /// Use this strategy when it's important to process every reconciliation request, such as when
    /// handling rapid successive updates or when reconciliation side effects must be applied for
    /// each state transition. This provides stronger eventual consistency guarantees at the cost
    /// of increased queue operations and slightly higher latency.
    /// </para>
    /// </remarks>
    RequeueAfterDelay,

    /// <summary>
    /// Waits synchronously for the current reconciliation to complete before processing the new request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This strategy blocks the processing thread until the ongoing reconciliation completes, then
    /// immediately processes the new request. This ensures strict sequential processing of all
    /// reconciliation requests for a given entity UID, providing the strongest consistency guarantees.
    /// </para>
    /// <para>
    /// Use this strategy when reconciliation operations must be strictly serialized and processing
    /// order is critical. Note that this may reduce overall parallelism and throughput, as threads
    /// are blocked waiting for locks. This is most appropriate for entities with critical ordering
    /// requirements or non-idempotent reconciliation logic.
    /// </para>
    /// </remarks>
    WaitForCompletion,
}
