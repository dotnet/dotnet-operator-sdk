// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Configures a <see cref="ParallelReconciliationSettings"/> instance.
/// Set properties directly or use the fluent <c>With*</c> extension methods from
/// <see cref="ParallelReconciliationSettingsBuilderExtensions"/>, then call <see cref="Build"/>
/// to obtain the immutable <see cref="ParallelReconciliationSettings"/> record.
/// </summary>
public sealed class ParallelReconciliationSettingsBuilder
{
    private int _maxParallelReconciliations = Environment.ProcessorCount * 2;

    /// <summary>
    /// Gets or sets the maximum number of parallel reconciliations across all entities.
    /// Defaults to <c>Environment.ProcessorCount * 2</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than or equal to 0.</exception>
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
    /// Gets or sets the strategy for handling concurrent reconciliation requests for the same entity UID.
    /// Defaults to <see cref="ParallelReconciliationConflictStrategy.WaitForCompletion"/>.
    /// </summary>
    public ParallelReconciliationConflictStrategy ConflictStrategy { get; set; } =
        ParallelReconciliationConflictStrategy.WaitForCompletion;

    /// <summary>
    /// Gets or sets the delay before requeueing an entity when <see cref="ConflictStrategy"/> is
    /// <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>.
    /// <see langword="null"/> uses the default of 5 seconds.
    /// </summary>
    public TimeSpan? RequeueDelay { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of automatic error retries before an entry is dropped.
    /// Defaults to <c>5</c>. Set to <c>0</c> to disable retries.
    /// </summary>
    public int MaxErrorRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base duration for exponential back-off between error retries.
    /// Defaults to <c>2 seconds</c>.
    /// </summary>
    public TimeSpan ErrorBackoffBase { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Produces an immutable <see cref="ParallelReconciliationSettings"/> record from the current configuration.
    /// </summary>
    /// <returns>A fully initialised <see cref="ParallelReconciliationSettings"/> record.</returns>
    public ParallelReconciliationSettings Build() => new()
    {
        MaxParallelReconciliations = MaxParallelReconciliations,
        ConflictStrategy = ConflictStrategy,
        RequeueDelay = RequeueDelay,
        MaxErrorRetries = MaxErrorRetries,
        ErrorBackoffBase = ErrorBackoffBase,
    };
}
