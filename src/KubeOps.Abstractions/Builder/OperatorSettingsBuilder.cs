// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Configures an <see cref="OperatorSettings"/> instance.
/// Set properties directly or use the fluent <c>With*</c> extension methods,
/// then call <see cref="Build"/> to obtain the immutable <see cref="OperatorSettings"/> record.
/// </summary>
public sealed partial class OperatorSettingsBuilder
{
    private const string DefaultOperatorName = "KubernetesOperator";
    private const string NonCharReplacement = "-";

    /// <summary>
    /// The name of the operator that appears in logs and other elements.
    /// Defaults to <c>"kubernetesoperator"</c>.
    /// </summary>
    public string Name { get; set; } = OperatorNameRegex()
        .Replace(DefaultOperatorName, NonCharReplacement)
        .ToLowerInvariant();

    /// <summary>
    /// Controls the namespace which is watched by the operator.
    /// If this field is <c>null</c>, all namespaces are watched for CRD instances.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Defines the type of leader election mechanism to be used by the operator.
    /// Determines how resources and controllers are coordinated in a distributed environment.
    /// </summary>
    public LeaderElectionType LeaderElectionType { get; set; } = LeaderElectionType.None;

    /// <summary>
    /// Defines the strategy for queuing reconciliation events within the operator.
    /// Determines how reconciliation events are managed and queued during operator execution.
    /// </summary>
    public QueueStrategy QueueStrategy { get; set; } = QueueStrategy.InMemory;

    /// <summary>
    /// Defines how long one lease is valid for any leader.
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

    /// <summary>
    /// Indicates whether finalizers should be automatically attached to Kubernetes entities during reconciliation.
    /// When enabled, the operator will ensure that all defined finalizers for the entity are added if they are not already present.
    /// </summary>
    public bool AutoAttachFinalizers { get; set; } = true;

    /// <summary>
    /// Indicates whether finalizers should be automatically removed from Kubernetes resources
    /// upon successful completion of their finalization process.
    /// </summary>
    public bool AutoDetachFinalizers { get; set; } = true;

    /// <summary>
    /// Defines the strategy used to decide whether a watch event should trigger reconciliation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ReconcileStrategy.ByGeneration"/> skips watch events that do not increase
    /// <c>metadata.generation</c>. Label, annotation, and other metadata-only writes never increment
    /// generation. Status updates do not increment generation when the CRD has a status subresource
    /// enabled. This matches standard Kubernetes controller behaviour.
    /// </para>
    /// <para>
    /// <see cref="ReconcileStrategy.ByResourceVersion"/> triggers reconciliation whenever
    /// <c>metadata.resourceVersion</c> changes, which occurs on every successful API server write
    /// regardless of which field changed (spec, status, labels, annotations, finalizers).
    /// Choose this strategy when your controller must react to changes outside the spec.
    /// </para>
    /// </remarks>
    public ReconcileStrategy ReconcileStrategy { get; set; } = ReconcileStrategy.ByGeneration;

    /// <summary>
    /// Gets or sets the configuration options for parallel reconciliation processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These options enable fine-grained control over the reconciliation loop's parallelism and
    /// concurrency behavior. The settings affect how the operator balances throughput (processing
    /// multiple entities simultaneously) with consistency (preventing race conditions on individual entities).
    /// </para>
    /// <para>
    /// By default, the operator uses <see cref="ParallelReconciliationConflictStrategy.WaitForCompletion"/>
    /// and allows up to <c>Environment.ProcessorCount * 2</c> concurrent reconciliations.
    /// The <c>WaitForCompletion</c> strategy ensures that no reconciliation requests are lost by waiting
    /// for any in-progress reconciliation to complete before processing the next request for the same entity.
    /// Adjust these values based on your reconciliation logic complexity, external API rate limits,
    /// and cluster resource constraints.
    /// </para>
    /// </remarks>
    /// <seealso cref="ParallelReconciliationOptions"/>
    /// <seealso cref="ParallelReconciliationConflictStrategy"/>
    public ParallelReconciliationOptions ParallelReconciliationOptions { get; set; } = new();

    /// <summary>
    /// Produces an immutable <see cref="OperatorSettings"/> record from the current configuration.
    /// </summary>
    /// <returns>A fully initialised <see cref="OperatorSettings"/> record.</returns>
    public OperatorSettings Build() => new()
    {
        Name = Name,
        Namespace = Namespace,
        LeaderElectionType = LeaderElectionType,
        QueueStrategy = QueueStrategy,
        LeaderElectionLeaseDuration = LeaderElectionLeaseDuration,
        LeaderElectionRenewDeadline = LeaderElectionRenewDeadline,
        LeaderElectionRetryPeriod = LeaderElectionRetryPeriod,
        ConfigureResourceWatcherEntityCache = ConfigureResourceWatcherEntityCache,
        AutoAttachFinalizers = AutoAttachFinalizers,
        AutoDetachFinalizers = AutoDetachFinalizers,
        ReconcileStrategy = ReconcileStrategy,
        ParallelReconciliationOptions = ParallelReconciliationOptions,
    };

    [GeneratedRegex(@"(\W|_)", RegexOptions.CultureInvariant)]
    private static partial Regex OperatorNameRegex();
}
