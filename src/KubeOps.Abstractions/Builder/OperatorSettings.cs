// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Operator settings.
/// </summary>
public sealed partial class OperatorSettings
{
    private const string DefaultOperatorName = "KubernetesOperator";
    private const string NonCharReplacement = "-";

    /// <summary>
    /// The name of the operator that appears in logs and other elements.
    /// Defaults to "kubernetesoperator" when not set.
    /// </summary>
    public string Name { get; set; } =
        OperatorNameRegex().Replace(
                DefaultOperatorName,
                NonCharReplacement)
            .ToLowerInvariant();

    /// <summary>
    /// <para>
    /// Controls the namespace which is watched by the operator.
    /// If this field is left `null`, all namespaces are watched for
    /// CRD instances.
    /// </para>
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// <para>
    /// Whether the leader elector should run. You should enable
    /// this if you plan to run the operator redundantly.
    /// </para>
    /// <para>
    /// If this is disabled and an operator runs in multiple instances
    /// (in the same namespace), it can lead to a "split brain" problem.
    /// </para>
    /// <para>
    /// Defaults to `false`.
    /// </para>
    /// </summary>
    public bool EnableLeaderElection { get; set; } = false;

    /// <summary>
    /// Defines how long one lease is valid for any leader.
    /// Defaults to 15 seconds.
    /// </summary>
    public TimeSpan LeaderElectionLeaseDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// When the leader elector tries to refresh the leadership lease.
    /// </summary>
    public TimeSpan LeaderElectionRenewDeadline { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The wait timeout if the lease cannot be acquired.
    /// </summary>
    public TimeSpan LeaderElectionRetryPeriod { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Allows configuration of the FusionCache settings for resource watcher entity caching.
    /// This property is optional and can be used to customize caching behavior for resource watcher entities.
    /// If not set, a default cache configuration is applied.
    /// </summary>
    public Action<IFusionCacheBuilder>? ConfigureResourceWatcherEntityCache { get; set; }

    [GeneratedRegex(@"(\W|_)", RegexOptions.CultureInvariant)]
    private static partial Regex OperatorNameRegex();
}
