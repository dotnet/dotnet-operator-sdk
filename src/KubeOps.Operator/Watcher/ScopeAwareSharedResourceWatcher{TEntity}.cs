// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.LeaderElection;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Metrics;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.Logging;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Watcher;

/// <summary>
/// The scope-aware counterpart of <see cref="SharedResourceWatcher{TEntity}"/>: a single shared
/// watch whose events are dispatched to all pipelines of the entity type, but only for entities
/// the <see cref="ILeadershipScope"/> declares this instance responsible for.
/// </summary>
/// <typeparam name="TEntity">The type of the Kubernetes entity being watched.</typeparam>
/// <param name="activitySource">The activity source used to trace watch events.</param>
/// <param name="logger">The logger.</param>
/// <param name="cacheProvider">The FusionCache provider for the event deduplication cache.</param>
/// <param name="entityQueue">The queue that receives the watched entities for reconciliation.</param>
/// <param name="settings">The operator settings.</param>
/// <param name="labelSelector">The label selector that restricts the watched entities.</param>
/// <param name="fieldSelector">The field selector that restricts the watched entities.</param>
/// <param name="client">The Kubernetes client.</param>
/// <param name="leadershipScope">The scope that decides which entities this instance is responsible for.</param>
/// <param name="dispatcher">The dispatcher that forwards each event to the matching controller pipelines.</param>
/// <param name="scopeFactory">The factory for the logging scopes of watched entities.</param>
/// <param name="metrics">The operator metrics, or <c>null</c> when metrics are disabled.</param>
internal sealed class ScopeAwareSharedResourceWatcher<TEntity>(
    ActivitySource activitySource,
    ILogger<ScopeAwareSharedResourceWatcher<TEntity>> logger,
    IFusionCacheProvider cacheProvider,
    ITimedEntityQueue<TEntity> entityQueue,
    OperatorSettings settings,
    IEntityLabelSelector<TEntity> labelSelector,
    IEntityFieldSelector<TEntity> fieldSelector,
    IKubernetesClient client,
    ILeadershipScope leadershipScope,
    SharedPipelineDispatcher<TEntity> dispatcher,
    IEntityLoggingScopeFactory<TEntity> scopeFactory,
    OperatorMetrics? metrics = null)
    : ScopeAwareResourceWatcher<TEntity>(
        activitySource,
        logger,
        cacheProvider,
        entityQueue,
        settings,
        labelSelector,
        fieldSelector,
        client,
        leadershipScope,
        scopeFactory,
        cachePartition: string.Empty,
        metrics)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    protected override Task OnScopedEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken) =>
        dispatcher.ProcessEventAsync(eventType, entity, this, cancellationToken);

    /// <inheritdoc/>
    protected override void OnWatchSessionStarting(bool isFullRelist)
    {
        if (isFullRelist)
        {
            dispatcher.ResetMembership();
        }
    }
}
