// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;
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

    private bool _immutable;

    private string _name = OperatorNameRegex()
        .Replace(DefaultOperatorName, NonCharReplacement)
        .ToLowerInvariant();

    private string? _namespace;

    private LeaderElectionType _leaderElectionType = LeaderElectionType.None;

    private QueueStrategy _queueStrategy = QueueStrategy.InMemory;

    private TimeSpan _leaderElectionLeaseDuration = TimeSpan.FromSeconds(15);

    private TimeSpan _leaderElectionRenewDeadline = TimeSpan.FromSeconds(10);

    private TimeSpan _leaderElectionRetryPeriod = TimeSpan.FromSeconds(2);

    private Action<IFusionCacheBuilder>? _configureResourceWatcherEntityCache;

    private bool _autoAttachFinalizers = true;

    private bool _autoDetachFinalizers = true;

    private ReconcileStrategy _reconcileStrategy = ReconcileStrategy.ByGeneration;

    private ParallelReconciliationOptions _parallelReconciliationOptions = new();

    /// <summary>
    /// The name of the operator that appears in logs and other elements.
    /// Defaults to "kubernetesoperator" when not set.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            EnsureMutable();
            _name = value;
        }
    }

    /// <summary>
    /// <para>
    /// Controls the namespace which is watched by the operator.
    /// If this field is left `null`, all namespaces are watched for
    /// CRD instances.
    /// </para>
    /// </summary>
    public string? Namespace
    {
        get => _namespace;
        set
        {
            EnsureMutable();
            _namespace = value;
        }
    }

    /// <summary>
    /// Defines the type of leader election mechanism to be used by the operator.
    /// Determines how resources and controllers are coordinated in a distributed environment.
    /// Defaults to <see cref="LeaderElectionType.None"/> indicating no leader election is configured.
    /// </summary>
    public LeaderElectionType LeaderElectionType
    {
        get => _leaderElectionType;
        set
        {
            EnsureMutable();
            _leaderElectionType = value;
        }
    }

    /// <summary>
    /// Defines the strategy for queuing reconciliation events within the operator.
    /// Determines how reconciliation events are managed and queued during operator execution.
    /// Defaults to <see cref="QueueStrategy.InMemory"/> when not explicitly configured.
    /// </summary>
    public QueueStrategy QueueStrategy
    {
        get => _queueStrategy;
        set
        {
            EnsureMutable();
            _queueStrategy = value;
        }
    }

    /// <summary>
    /// Defines how long one lease is valid for any leader.
    /// Defaults to 15 seconds.
    /// </summary>
    public TimeSpan LeaderElectionLeaseDuration
    {
        get => _leaderElectionLeaseDuration;
        set
        {
            EnsureMutable();
            _leaderElectionLeaseDuration = value;
        }
    }

    /// <summary>
    /// When the leader elector tries to refresh the leadership lease.
    /// </summary>
    public TimeSpan LeaderElectionRenewDeadline
    {
        get => _leaderElectionRenewDeadline;
        set
        {
            EnsureMutable();
            _leaderElectionRenewDeadline = value;
        }
    }

    /// <summary>
    /// The wait timeout if the lease cannot be acquired.
    /// </summary>
    public TimeSpan LeaderElectionRetryPeriod
    {
        get => _leaderElectionRetryPeriod;
        set
        {
            EnsureMutable();
            _leaderElectionRetryPeriod = value;
        }
    }

    /// <summary>
    /// Allows configuration of the FusionCache settings for resource watcher entity caching.
    /// This property is optional and can be used to customize caching behavior for resource watcher entities.
    /// If not set, a default cache configuration is applied.
    /// </summary>
    public Action<IFusionCacheBuilder>? ConfigureResourceWatcherEntityCache
    {
        get => _configureResourceWatcherEntityCache;
        set
        {
            EnsureMutable();
            _configureResourceWatcherEntityCache = value;
        }
    }

    /// <summary>
    /// Indicates whether finalizers should be automatically attached to Kubernetes entities during reconciliation.
    /// When enabled, the operator will ensure that all defined finalizers for the entity are added if they are not already present.
    /// Defaults to true.
    /// </summary>
    public bool AutoAttachFinalizers
    {
        get => _autoAttachFinalizers;
        set
        {
            EnsureMutable();
            _autoAttachFinalizers = value;
        }
    }

    /// <summary>
    /// Indicates whether finalizers should be automatically removed from Kubernetes resources
    /// upon successful completion of their finalization process. Defaults to true.
    /// </summary>
    public bool AutoDetachFinalizers
    {
        get => _autoDetachFinalizers;
        set
        {
            EnsureMutable();
            _autoDetachFinalizers = value;
        }
    }

    /// <summary>
    /// Defines the strategy used to decide whether a watch event should trigger reconciliation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ReconcileStrategy.ByGeneration"/> (the default) skips watch events that do not
    /// increase <c>metadata.generation</c>. Label, annotation, and other metadata-only writes
    /// never increment generation. Status updates do not increment generation when the CRD has
    /// a status subresource enabled. This matches standard Kubernetes controller behaviour.
    /// </para>
    /// <para>
    /// <see cref="ReconcileStrategy.ByResourceVersion"/> triggers reconciliation whenever
    /// <c>metadata.resourceVersion</c> changes, which occurs on every successful API server write
    /// regardless of which field changed (spec, status, labels, annotations, finalizers).
    /// Choose this strategy when your controller must react to changes outside the spec.
    /// </para>
    /// </remarks>
    public ReconcileStrategy ReconcileStrategy
    {
        get => _reconcileStrategy;
        set
        {
            EnsureMutable();
            _reconcileStrategy = value;
        }
    }

    /// <summary>
    /// Gets or sets the configuration options for parallel reconciliation processing.
    /// </summary>
    /// <value>
    /// The configuration options that control how reconciliation requests are processed in parallel,
    /// including the maximum concurrency level and the strategy for handling conflicts when the same
    /// entity is being reconciled multiple times. The default is a new instance with default values.
    /// </value>
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
    public ParallelReconciliationOptions ParallelReconciliationOptions
    {
        get => _parallelReconciliationOptions;
        set
        {
            EnsureMutable();
            _parallelReconciliationOptions = value;
        }
    }

    /// <summary>
    /// Makes these settings immutable. Once called, any attempt to set a property will throw
    /// an <see cref="InvalidOperationException"/>. This is called automatically by
    /// <c>AddKubernetesOperator</c> after the configure action has been invoked.
    /// </summary>
    public void MakeImmutable() => _immutable = true;

    [GeneratedRegex(@"(\W|_)", RegexOptions.CultureInvariant)]
    private static partial Regex OperatorNameRegex();

    private void EnsureMutable([CallerMemberName] string? property = null)
    {
        if (_immutable)
        {
            throw new InvalidOperationException(
                $"OperatorSettings are immutable after the operator has been built (property: '{property}'). " +
                "Configure settings via the Action<OperatorSettings> delegate in AddKubernetesOperator.");
        }
    }
}
