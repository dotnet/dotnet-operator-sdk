// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Fluent extension methods for <see cref="ParallelReconciliationSettingsBuilder"/>.
/// Each method sets one property and returns the same builder instance for chaining.
/// </summary>
public static class ParallelReconciliationSettingsBuilderExtensions
{
    /// <summary>Sets the maximum number of parallel reconciliations across all entities.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="value">The maximum concurrency level. Must be greater than zero.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static ParallelReconciliationSettingsBuilder WithMaxParallelReconciliations(
        this ParallelReconciliationSettingsBuilder builder, int value)
    {
        builder.MaxParallelReconciliations = value;
        return builder;
    }

    /// <summary>Sets the conflict strategy for concurrent reconciliation attempts on the same entity UID.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="strategy">The conflict strategy.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static ParallelReconciliationSettingsBuilder WithConflictStrategy(
        this ParallelReconciliationSettingsBuilder builder, ParallelReconciliationConflictStrategy strategy)
    {
        builder.ConflictStrategy = strategy;
        return builder;
    }

    /// <summary>Sets the requeue delay used by <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="delay">The delay, or <c>null</c> to use the default of 5 seconds.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static ParallelReconciliationSettingsBuilder WithRequeueDelay(
        this ParallelReconciliationSettingsBuilder builder, TimeSpan? delay)
    {
        builder.RequeueDelay = delay;
        return builder;
    }

    /// <summary>Sets the maximum number of automatic error retries before an entry is dropped.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="retries">The retry limit. Pass <c>0</c> to disable retries.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static ParallelReconciliationSettingsBuilder WithMaxErrorRetries(
        this ParallelReconciliationSettingsBuilder builder, int retries)
    {
        builder.MaxErrorRetries = retries;
        return builder;
    }

    /// <summary>Sets the base duration for exponential back-off between error retries.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="backoff">The base back-off duration.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static ParallelReconciliationSettingsBuilder WithErrorBackoffBase(
        this ParallelReconciliationSettingsBuilder builder, TimeSpan backoff)
    {
        builder.ErrorBackoffBase = backoff;
        return builder;
    }
}
