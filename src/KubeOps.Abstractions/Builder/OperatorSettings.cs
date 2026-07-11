// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Immutable operator settings. Created exclusively via <see cref="OperatorSettingsBuilder.Build"/>.
/// All non-nullable properties are <c>required</c>; use <see cref="OperatorSettingsBuilder"/> to
/// construct a fully initialised instance with correct defaults.
/// </summary>
public sealed record OperatorSettings
{
    /// <summary>
    /// The name of the operator that appears in logs and other elements.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// <para>
    /// Controls the namespace which is watched by the operator.
    /// If this field is <c>null</c>, all namespaces are watched for CRD instances.
    /// </para>
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Defines the type of leader election mechanism to be used by the operator.
    /// Determines how resources and controllers are coordinated in a distributed environment.
    /// </summary>
    public required LeaderElectionType LeaderElectionType { get; init; }

    /// <summary>
    /// Defines the strategy for queuing reconciliation events within the operator.
    /// Determines how reconciliation events are managed and queued during operator execution.
    /// </summary>
    public required QueueStrategy QueueStrategy { get; init; }

    /// <summary>
    /// Defines how the operator creates watch connections to the Kubernetes API server.
    /// <see cref="WatchStrategy.PerController"/> (default) opens one watch per registered controller
    /// with server-side selectors; <see cref="WatchStrategy.SharedPerEntity"/> shares one watch per
    /// entity type and dispatches events to all matching controllers client-side.
    /// </summary>
    public required WatchStrategy WatchStrategy { get; init; }

    /// <summary>
    /// Defines how long one lease is valid for any leader.
    /// </summary>
    public required TimeSpan LeaderElectionLeaseDuration { get; init; }

    /// <summary>
    /// When the leader elector tries to refresh the leadership lease.
    /// </summary>
    public required TimeSpan LeaderElectionRenewDeadline { get; init; }

    /// <summary>
    /// The wait timeout if the lease cannot be acquired.
    /// </summary>
    public required TimeSpan LeaderElectionRetryPeriod { get; init; }

    /// <summary>
    /// Allows configuration of the FusionCache settings for resource watcher entity caching.
    /// This property is optional and can be used to customize caching behavior for resource watcher entities.
    /// If not set, a default cache configuration is applied.
    /// </summary>
    public Action<IFusionCacheBuilder>? ConfigureResourceWatcherEntityCache { get; init; }

    /// <summary>
    /// Indicates whether finalizers should be automatically attached to Kubernetes entities during reconciliation.
    /// When enabled, the operator will ensure that all defined finalizers for the entity are added if they are not already present.
    /// </summary>
    public required bool AutoAttachFinalizers { get; init; }

    /// <summary>
    /// Indicates whether finalizers should be automatically removed from Kubernetes resources
    /// upon successful completion of their finalization process.
    /// </summary>
    public required bool AutoDetachFinalizers { get; init; }

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
    public required ReconcileStrategy ReconcileStrategy { get; init; }

    /// <summary>
    /// Gets the settings for parallel reconciliation processing.
    /// </summary>
    /// <value>
    /// The settings that control how reconciliation requests are processed in parallel,
    /// including the maximum concurrency level and the strategy for handling conflicts when the same
    /// entity is being reconciled multiple times.
    /// </value>
    /// <remarks>
    /// <para>
    /// These settings enable fine-grained control over the reconciliation loop's parallelism and
    /// concurrency behavior. They affect how the operator balances throughput (processing
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
    /// <seealso cref="ParallelReconciliationSettings"/>
    /// <seealso cref="ParallelReconciliationConflictStrategy"/>
    public required ParallelReconciliationSettings ParallelReconciliation { get; init; }

    /// <summary>
    /// Indicates whether the operator collects OpenTelemetry metrics (queue, watcher, and
    /// reconciliation instruments) via a <see cref="System.Diagnostics.Metrics.Meter"/> named
    /// after <see cref="Name"/>.
    /// </summary>
    /// <remarks>
    /// Collecting metrics is virtually free when no listener/exporter is attached. To actually
    /// scrape the metrics, register an OpenTelemetry exporter for the meter named <see cref="Name"/>.
    /// </remarks>
    public required bool EnableMetrics { get; init; }

    /// <summary>
    /// Indicates whether the operator validates, on host startup, that its dependency injection
    /// registrations are complete and consistent with the configured
    /// <see cref="LeaderElectionType"/> and <see cref="QueueStrategy"/>. Disabled by default.
    /// </summary>
    /// <remarks>
    /// When enabled, the operator verifies — for every managed entity — that the components implied by
    /// the configuration are registered. If anything is missing, host startup aborts with an
    /// <c>InvalidRegistrationException</c> listing the gaps. This catches registration mistakes
    /// (for example a forgotten queue or queue consumer in a manually wired custom-queue setup) that would
    /// otherwise let the operator start without processing any resources.
    /// </remarks>
    public bool ValidateRegistrations { get; init; }
}
