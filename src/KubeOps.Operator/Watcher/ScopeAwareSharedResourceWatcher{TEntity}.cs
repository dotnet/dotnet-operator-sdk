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
