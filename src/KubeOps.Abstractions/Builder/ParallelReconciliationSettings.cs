// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Immutable parallel reconciliation settings. Created exclusively via <see cref="ParallelReconciliationSettingsBuilder.Build"/>.
/// All non-nullable properties are <c>required</c>; use <see cref="ParallelReconciliationSettingsBuilder"/> to
/// construct a fully initialised instance with correct defaults.
/// </summary>
/// <seealso cref="ParallelReconciliationSettingsBuilder"/>
/// <seealso cref="ParallelReconciliationConflictStrategy"/>
public sealed record ParallelReconciliationSettings
{
    /// <summary>
    /// Gets the maximum number of parallel reconciliations across all entities.
    /// </summary>
    /// <value>
    /// The maximum number of entities that can be reconciled concurrently.
    /// The default is twice the number of processor cores (<see cref="Environment.ProcessorCount"/> * 2).
    /// </value>
    public required int MaxParallelReconciliations { get; init; }

    /// <summary>
    /// Gets the strategy for handling reconciliation requests when an entity with the same UID is already being processed.
    /// </summary>
    /// <value>
    /// One of the enumeration values that specifies how to handle concurrent reconciliation attempts for the same entity.
    /// The default is <see cref="ParallelReconciliationConflictStrategy.WaitForCompletion"/>.
    /// </value>
    /// <seealso cref="ParallelReconciliationConflictStrategy"/>
    public required ParallelReconciliationConflictStrategy ConflictStrategy { get; init; }

    /// <summary>
    /// Gets the delay before requeueing an entity when <see cref="ConflictStrategy"/> is set to <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>.
    /// </summary>
    /// <value>
    /// The time span to wait before requeueing a conflicting reconciliation request, or <see langword="null"/> to use the
    /// default delay of 5 seconds. Only used when <see cref="ConflictStrategy"/> is <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>.
    /// </value>
    public TimeSpan? RequeueDelay { get; init; }

    /// <summary>
    /// Gets the maximum number of times a failed reconciliation is automatically retried before the entry is permanently dropped.
    /// </summary>
    /// <value>
    /// The maximum number of retry attempts after an error. The default is <c>5</c>.
    /// Set to <c>0</c> to disable automatic error retries entirely.
    /// </value>
    /// <seealso cref="ErrorBackoffBase"/>
    public required int MaxErrorRetries { get; init; }

    /// <summary>
    /// Gets the base duration used to compute the exponential back-off delay between successive error retries.
    /// </summary>
    /// <value>
    /// The base time span for exponential back-off. The default is <c>2 seconds</c>.
    /// The actual delay for retry <c>n</c> is <c>ErrorBackoffBase * 2^(n-1)</c>.
    /// </value>
    /// <seealso cref="MaxErrorRetries"/>
    public required TimeSpan ErrorBackoffBase { get; init; }

    /// <summary>
    /// Gets the effective requeue delay, using a default value if <see cref="RequeueDelay"/> is <see langword="null"/>.
    /// </summary>
    /// <returns>
    /// The configured <see cref="RequeueDelay"/> if set; otherwise, a default of 5 seconds.
    /// </returns>
    public TimeSpan GetEffectiveRequeueDelay() => RequeueDelay ?? TimeSpan.FromSeconds(5);

    /// <summary>
    /// Computes the back-off delay for a given error-retry attempt using exponential back-off.
    /// </summary>
    /// <param name="retryCount">The 1-based index of the retry attempt.</param>
    /// <returns>
    /// The delay before the next retry attempt, calculated as <c>ErrorBackoffBase * 2^(retryCount-1)</c>.
    /// </returns>
    public TimeSpan GetErrorBackoffDelay(int retryCount)
    {
        // 2^(retryCount-1) grows quickly; cap the exponent at 30 to avoid TimeSpan overflow.
        var exponent = Math.Min(retryCount - 1, 30);
        return ErrorBackoffBase * Math.Pow(2, exponent);
    }
}
