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
/// <param name="activitySource">The activity source used to trace watch events.</param>
/// <param name="logger">The logger.</param>
/// <param name="cacheProvider">The FusionCache provider for the event deduplication cache.</param>
/// <param name="entityQueue">The queue that receives the watched entities for reconciliation.</param>
/// <param name="settings">The operator settings.</param>
/// <param name="labelSelector">The label selector that restricts the watched entities.</param>
/// <param name="fieldSelector">The field selector that restricts the watched entities.</param>
/// <param name="client">The Kubernetes client.</param>
/// <param name="elector">The leader elector that starts and stops the watcher on leadership changes.</param>
/// <param name="dispatcher">The dispatcher that forwards each event to the matching controller pipelines.</param>
/// <param name="scopeFactory">The factory for the logging scopes of watched entities.</param>
/// <param name="metrics">The operator metrics, or <c>null</c> when metrics are disabled.</param>
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
