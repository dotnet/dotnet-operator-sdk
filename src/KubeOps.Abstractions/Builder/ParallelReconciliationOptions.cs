// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Provides configuration options for parallel reconciliation processing of Kubernetes entities.
/// </summary>
/// <remarks>
/// <para>
/// This configuration controls how the operator handles concurrent reconciliation requests
/// for multiple entities. The settings balance between throughput (maximum parallelism) and
/// consistency (UID-based locking to prevent concurrent reconciliation of the same entity).
/// </para>
/// <para>
/// When an entity is being reconciled, subsequent reconciliation requests for the same UID
/// (Unique Identifier) are handled according to the configured <see cref="ConflictStrategy"/>.
/// This prevents race conditions and ensures data consistency during entity processing.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// // Example 1: Using default WaitForCompletion strategy
/// var options1 = new ParallelReconciliationOptions
/// {
///     MaxParallelReconciliations = 10
///     // ConflictStrategy defaults to WaitForCompletion
/// };
///
/// // Example 2: Using Discard strategy for higher throughput
/// var options2 = new ParallelReconciliationOptions
/// {
///     MaxParallelReconciliations = 10,
///     ConflictStrategy = ParallelReconciliationConflictStrategy.Discard
/// };
///
/// // Example 3: Using RequeueAfterDelay strategy with custom delay
/// var options3 = new ParallelReconciliationOptions
/// {
///     MaxParallelReconciliations = 10,
///     ConflictStrategy = ParallelReconciliationConflictStrategy.RequeueAfterDelay,
///     RequeueDelay = TimeSpan.FromSeconds(3) // Optional, defaults to 5 seconds
/// };
/// </code>
/// </example>
public sealed record ParallelReconciliationOptions
{
    private int _maxParallelReconciliations = Environment.ProcessorCount * 2;

    /// <summary>
    /// Gets or sets the maximum number of parallel reconciliations across all entities.
    /// </summary>
    /// <value>
    /// The maximum number of entities that can be reconciled concurrently.
    /// The default is twice the number of processor cores (<see cref="Environment.ProcessorCount"/> * 2).
    /// </value>
    /// <remarks>
    /// <para>
    /// This setting limits the total number of concurrent reconciliation operations to prevent
    /// resource exhaustion. A higher value increases throughput but consumes more CPU and memory.
    /// A lower value reduces resource usage but may increase latency for entity reconciliation.
    /// </para>
    /// <para>
    /// The default value is based on the assumption that reconciliation operations are I/O-bound
    /// (e.g., making API calls to Kubernetes), which typically allows for higher parallelism
    /// than CPU-bound operations.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is less than or equal to 0.
    /// </exception>
    public int MaxParallelReconciliations
    {
        get => _maxParallelReconciliations;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            _maxParallelReconciliations = value;
        }
    }

    /// <summary>
    /// Gets or sets the strategy for handling reconciliation requests when an entity with the same UID is already being processed.
    /// </summary>
    /// <value>
    /// One of the enumeration values that specifies how to handle concurrent reconciliation attempts for the same entity.
    /// The default is <see cref="ParallelReconciliationConflictStrategy.WaitForCompletion"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// Each Kubernetes entity has a unique identifier (UID). When the operator receives multiple reconciliation
    /// requests for the same UID while a reconciliation is already in progress, this strategy determines the behavior:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="ParallelReconciliationConflictStrategy.WaitForCompletion"/>: Blocks until the current reconciliation
    /// completes, then processes the request sequentially. This ensures no reconciliation requests are lost.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="ParallelReconciliationConflictStrategy.Discard"/>: Ignores the new request, assuming the current
    /// reconciliation will handle the latest state. This is the most performant option.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>: Requeues the request to be processed
    /// after the configured <see cref="RequeueDelay"/>, ensuring no updates are lost while avoiding immediate blocking.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <seealso cref="ParallelReconciliationConflictStrategy"/>
    public ParallelReconciliationConflictStrategy ConflictStrategy { get; set; } = ParallelReconciliationConflictStrategy.WaitForCompletion;

    /// <summary>
    /// Gets or sets the delay before requeueing an entity when <see cref="ConflictStrategy"/> is set to <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>.
    /// </summary>
    /// <value>
    /// The time span to wait before requeueing a conflicting reconciliation request, or <see langword="null"/> to use the default delay.
    /// When <see langword="null"/> and <see cref="ConflictStrategy"/> is <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>,
    /// a default delay of 5 seconds is used. The default is <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// This property is only used when <see cref="ConflictStrategy"/> is set to
    /// <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>. When using other strategies,
    /// this value is ignored.
    /// </para>
    /// <para>
    /// The delay provides a grace period for the current reconciliation to complete, reducing the likelihood of
    /// immediate re-conflicts. A longer delay reduces system load but increases latency for
    /// processing entity updates. A shorter delay provides faster response but may result in
    /// more frequent requeueing if reconciliations take longer than the delay.
    /// </para>
    /// </remarks>
    public TimeSpan? RequeueDelay { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of times a failed reconciliation is automatically retried
    /// before the entry is permanently dropped.
    /// </summary>
    /// <value>
    /// The maximum number of retry attempts after an error. The default is <c>5</c>.
    /// Set to <c>0</c> to disable automatic error retries entirely.
    /// </value>
    /// <remarks>
    /// <para>
    /// When a reconciliation throws an unhandled exception, the operator requeues the entry with an
    /// exponential back-off delay (see <see cref="ErrorBackoffBase"/>). Once the number of retries
    /// reaches this limit the entry is discarded and only an error is logged. This prevents a
    /// non-transient failure from causing an infinite retry loop.
    /// </para>
    /// </remarks>
    /// <seealso cref="ErrorBackoffBase"/>
    public int MaxErrorRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base duration used to compute the exponential back-off delay between
    /// successive error retries.
    /// </summary>
    /// <value>
    /// The base time span for exponential back-off. The default is <c>2 seconds</c>.
    /// </value>
    /// <remarks>
    /// <para>
    /// The actual delay for retry attempt <c>n</c> (1-based) is calculated as
    /// <c>ErrorBackoffBase * 2^(n-1)</c>. For example, with the default of 2 seconds:
    /// attempt 1 waits 2 s, attempt 2 waits 4 s, attempt 3 waits 8 s, and so on.
    /// </para>
    /// <para>
    /// Choose a base that fits the expected duration of transient failures (e.g., temporary
    /// network outages or API server unavailability). Very short bases may cause excessive load
    /// during outages; very long bases may delay recovery unnecessarily.
    /// </para>
    /// </remarks>
    /// <seealso cref="MaxErrorRetries"/>
    public TimeSpan ErrorBackoffBase { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the effective requeue delay, using a default value if <see cref="RequeueDelay"/> is <see langword="null"/>.
    /// </summary>
    /// <returns>
    /// The configured <see cref="RequeueDelay"/> if set; otherwise, a default of 5 seconds.
    /// </returns>
    /// <remarks>
    /// This method is useful when implementing the requeueing logic, as it provides a sensible
    /// default value when the delay is not explicitly configured.
    /// </remarks>
    public TimeSpan GetEffectiveRequeueDelay() => RequeueDelay ?? TimeSpan.FromSeconds(5);

    /// <summary>
    /// Computes the back-off delay for a given error-retry attempt using exponential back-off.
    /// </summary>
    /// <param name="retryCount">The 1-based index of the retry attempt.</param>
    /// <returns>
    /// The delay before the next retry attempt, calculated as <c>ErrorBackoffBase * 2^(retryCount-1)</c>.
    /// </returns>
    /// <example>
    /// <code language="csharp">
    /// var options = new ParallelReconciliationOptions { ErrorBackoffBase = TimeSpan.FromSeconds(2) };
    /// // Retry 1 → 2 s, Retry 2 → 4 s, Retry 3 → 8 s
    /// var delay = options.GetErrorBackoffDelay(retryCount: 2); // TimeSpan.FromSeconds(4)
    /// </code>
    /// </example>
    public TimeSpan GetErrorBackoffDelay(int retryCount)
    {
        // 2^(retryCount-1) grows quickly; cap the exponent at 30 to avoid TimeSpan overflow.
        var exponent = Math.Min(retryCount - 1, 30);
        return ErrorBackoffBase * Math.Pow(2, exponent);
    }
}
