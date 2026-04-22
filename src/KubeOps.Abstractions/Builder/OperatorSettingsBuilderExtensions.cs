// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Fluent extension methods for <see cref="OperatorSettingsBuilder"/>.
/// Each method sets one property and returns the same builder instance for chaining.
/// </summary>
public static class OperatorSettingsBuilderExtensions
{
    /// <summary>Sets the operator name.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="name">The operator name.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithName(this OperatorSettingsBuilder builder, string name)
    {
        builder.Name = name;
        return builder;
    }

    /// <summary>Sets the namespace to watch. Pass <c>null</c> to watch all namespaces (the default).</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="ns">The namespace, or <c>null</c> for all namespaces.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithNamespace(this OperatorSettingsBuilder builder, string? ns)
    {
        builder.Namespace = ns;
        return builder;
    }

    /// <summary>Sets the leader election type.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="type">The leader election type.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithLeaderElection(this OperatorSettingsBuilder builder, LeaderElectionType type)
    {
        builder.LeaderElectionType = type;
        return builder;
    }

    /// <summary>Sets the queue strategy.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="strategy">The queue strategy.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithQueueStrategy(this OperatorSettingsBuilder builder, QueueStrategy strategy)
    {
        builder.QueueStrategy = strategy;
        return builder;
    }

    /// <summary>
    /// Sets all three leader-election timing parameters in a single call.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="leaseDuration">How long a lease is valid for any leader.</param>
    /// <param name="renewDeadline">When the leader elector tries to refresh the leadership lease.</param>
    /// <param name="retryPeriod">The wait timeout if the lease cannot be acquired.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithLeaderElectionTimings(
        this OperatorSettingsBuilder builder,
        TimeSpan leaseDuration,
        TimeSpan renewDeadline,
        TimeSpan retryPeriod)
    {
        builder.LeaderElectionLeaseDuration = leaseDuration;
        builder.LeaderElectionRenewDeadline = renewDeadline;
        builder.LeaderElectionRetryPeriod = retryPeriod;
        return builder;
    }

    /// <summary>Sets how long a leader election lease is valid.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="leaseDuration">How long a lease is valid for any leader.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithLeaderElectionLeaseDuration(
        this OperatorSettingsBuilder builder, TimeSpan leaseDuration)
    {
        builder.LeaderElectionLeaseDuration = leaseDuration;
        return builder;
    }

    /// <summary>Sets the deadline by which the leader must renew its lease.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="renewDeadline">When the leader elector tries to refresh the leadership lease.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithLeaderElectionRenewDeadline(
        this OperatorSettingsBuilder builder, TimeSpan renewDeadline)
    {
        builder.LeaderElectionRenewDeadline = renewDeadline;
        return builder;
    }

    /// <summary>Sets the interval between leader election retry attempts.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="retryPeriod">The wait timeout if the lease cannot be acquired.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithLeaderElectionRetryPeriod(
        this OperatorSettingsBuilder builder, TimeSpan retryPeriod)
    {
        builder.LeaderElectionRetryPeriod = retryPeriod;
        return builder;
    }

    /// <summary>Configures the FusionCache builder used for resource watcher entity caching.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The FusionCache configuration action.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithResourceWatcherEntityCaching(
        this OperatorSettingsBuilder builder,
        Action<IFusionCacheBuilder> configure)
    {
        builder.ConfigureResourceWatcherEntityCache = configure;
        return builder;
    }

    /// <summary>Sets whether finalizers are automatically attached before reconciliation.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="value"><c>true</c> to auto-attach (default); <c>false</c> to disable.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithAutoAttachFinalizers(
        this OperatorSettingsBuilder builder, bool value = true)
    {
        builder.AutoAttachFinalizers = value;
        return builder;
    }

    /// <summary>Sets whether finalizers are automatically removed after finalization completes.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="value"><c>true</c> to auto-detach (default); <c>false</c> to disable.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithAutoDetachFinalizers(
        this OperatorSettingsBuilder builder, bool value = true)
    {
        builder.AutoDetachFinalizers = value;
        return builder;
    }

    /// <summary>Sets the reconcile strategy.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="strategy">The reconcile strategy.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithReconcileStrategy(
        this OperatorSettingsBuilder builder, ReconcileStrategy strategy)
    {
        builder.ReconcileStrategy = strategy;
        return builder;
    }

    /// <summary>Sets the parallel reconciliation options.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="options">The parallel reconciliation options.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static OperatorSettingsBuilder WithParallelReconciliation(
        this OperatorSettingsBuilder builder, ParallelReconciliationOptions options)
    {
        builder.ParallelReconciliationOptions = options;
        return builder;
    }
}
