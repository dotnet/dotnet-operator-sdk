// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using k8s;
using k8s.LeaderElection;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Metrics;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.Logging;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Watcher;

/// <summary>
/// Leadership-aware variant of <see cref="SharedResourceWatcher{TEntity}"/>: a shared watch connection
/// for multiple controller pipelines that only runs while this instance holds leadership.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type.</typeparam>
internal sealed class LeaderAwareSharedResourceWatcher<TEntity>(
    ActivitySource activitySource,
    ILogger<LeaderAwareSharedResourceWatcher<TEntity>> logger,
    IFusionCacheProvider cacheProvider,
    ITimedEntityQueue<TEntity> entityQueue,
    OperatorSettings settings,
    IEntityLabelSelector<TEntity> labelSelector,
    IEntityFieldSelector<TEntity> fieldSelector,
    IKubernetesClient client,
    LeaderElector elector,
    SharedPipelineDispatcher<TEntity> dispatcher,
    IEntityLoggingScopeFactory<TEntity> scopeFactory,
    OperatorMetrics? metrics = null)
    : LeaderAwareResourceWatcher<TEntity>(
        activitySource,
        logger,
        cacheProvider,
        entityQueue,
        settings,
        labelSelector,
        fieldSelector,
        client,
        elector,
        scopeFactory,
        cachePartition: string.Empty,
        metrics)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <inheritdoc/>
    protected override Task OnEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken) =>
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
