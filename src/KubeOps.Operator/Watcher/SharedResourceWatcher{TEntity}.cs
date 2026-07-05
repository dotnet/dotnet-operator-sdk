// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Metrics;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.Logging;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Watcher;

/// <summary>
/// A resource watcher shared by multiple controller pipelines of the same entity type
/// (<c>WatchStrategy.SharedPerEntity</c>). It maintains a single watch connection (without a
/// server-side label selector), deduplicates each event once, and dispatches it to every pipeline
/// whose label selector matches the entity — evaluated client-side by the
/// <see cref="SharedPipelineDispatcher{TEntity}"/>. Compared to one watcher per pipeline this reduces
/// API server connections and deduplication cache entries to one per entity type, independent of the
/// number of controllers.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type.</typeparam>
internal sealed class SharedResourceWatcher<TEntity>(
    ActivitySource activitySource,
    ILogger<SharedResourceWatcher<TEntity>> logger,
    IFusionCacheProvider cacheProvider,
    ITimedEntityQueue<TEntity> entityQueue,
    OperatorSettings settings,
    IEntityLabelSelector<TEntity> labelSelector,
    IEntityFieldSelector<TEntity> fieldSelector,
    IKubernetesClient client,
    SharedPipelineDispatcher<TEntity> dispatcher,
    OperatorMetrics? metrics = null)
    : ResourceWatcher<TEntity>(
        activitySource,
        logger,
        cacheProvider,
        entityQueue,
        settings,
        labelSelector,
        fieldSelector,
        client,
        cachePartition: string.Empty,
        metrics)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <inheritdoc/>
    protected override Task<bool> EnqueueEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken) =>
        dispatcher.DispatchAsync(eventType, entity, cancellationToken);
}
