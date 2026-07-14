// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Net;
using System.Runtime.Serialization;
using System.Text.Json;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Constants;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Metrics;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Reconciliation;
using KubeOps.Operator.Retry;

using Microsoft.Extensions.Logging;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Watcher;

public class ResourceWatcher<TEntity>(
    ActivitySource activitySource,
    ILogger<ResourceWatcher<TEntity>> logger,
    IFusionCacheProvider cacheProvider,
    ITimedEntityQueue<TEntity> entityQueue,
    OperatorSettings settings,
    IEntityLabelSelector<TEntity> labelSelector,
    IEntityFieldSelector<TEntity> fieldSelector,
    IKubernetesClient client,
    IEntityLoggingScopeFactory<TEntity> scopeFactory,
    string cachePartition = "",
    OperatorMetrics? metrics = null)
    : RestartableHostedService, ISharedWatchDedup<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly string[] _entityCacheTags = [typeof(TEntity).FullName ?? typeof(TEntity).Name];

    private uint _watcherReconnectRetries;

    /// <summary>
    /// Gets the tag applied to every cached deduplication entry of this entity type. The dedup cache is a single
    /// named FusionCache instance shared by all entity watchers (keyed by entity UID); the tag lets a
    /// leadership-aware subclass drop only <em>this</em> entity type's entries (see
    /// <see cref="LeaderAwareResourceWatcher{TEntity}"/>) instead of clearing every entity's entries.
    /// </summary>
    protected string EntityCacheTag => _entityCacheTags[0];

    /// <summary>
    /// Gets the fusion cache used to store a strategy-dependent deduplication token for each
    /// entity, preventing redundant reconciliations on duplicate watch events.
    /// </summary>
    /// <value>
    /// The <see cref="IFusionCache"/> instance for the active reconcile strategy:
    /// <see cref="ReconcileStrategy.ByGeneration"/> stores <c>metadata.generation</c> (<see langword="long"/>);
    /// <see cref="ReconcileStrategy.ByResourceVersion"/> stores <c>metadata.resourceVersion</c> (<see langword="string"/>).
    /// </value>
    /// <remarks>
    /// <para>
    /// Subclasses may access this cache to read or invalidate cached tokens. For example,
    /// <see cref="LeaderAwareResourceWatcher{TEntity}"/> removes this entity type's entries by
    /// <see cref="EntityCacheTag"/> when leadership is lost, so stale data is not carried over to the next watch
    /// session — without disturbing other entity types that share this cache instance.
    /// </para>
    /// <para>
    /// Note: when an entity has a <c>DeletionTimestamp</c> set (finalizer processing), the
    /// <see cref="ReconcileStrategy.ByGeneration"/> path bypasses the cache check and does
    /// not update the cached token. The cache therefore reflects the last value observed
    /// before deletion began, not the current state.
    /// </para>
    /// </remarks>
    protected IFusionCache EntityCache { get; } = cacheProvider.GetCache(
        CacheConstants.ResourceWatcherCacheNameFor(settings.ReconcileStrategy));

    /// <inheritdoc/>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting resource watcher for {ResourceType}.", typeof(TEntity).Name);
        var result = base.StartAsync(cancellationToken);
        logger.LogInformation("Started resource watcher for {ResourceType}.", typeof(TEntity).Name);
        return result;
    }

    /// <inheritdoc/>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping resource watcher for {ResourceType}.", typeof(TEntity).Name);
        return base.StopAsync(cancellationToken);
    }

    // The shared dispatcher consults the watcher's deduplication (see ISharedWatchDedup) to make the shared
    // dedup decision once per event while evaluating membership transitions before it. Implemented
    // explicitly so the dedup surface stays off the public API of this watcher.
    Task<bool> ISharedWatchDedup<TEntity>.IsDuplicateAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken) =>
        IsDuplicateAsync(eventType, entity, cancellationToken);

    Task ISharedWatchDedup<TEntity>.RecordDedupTokenAsync(TEntity entity, CancellationToken cancellationToken) =>
        RecordDedupTokenAsync(entity, cancellationToken);

    Task ISharedWatchDedup<TEntity>.RemoveDedupTokenAsync(TEntity entity, CancellationToken cancellationToken) =>
        RemoveDedupTokenAsync(entity, cancellationToken);

    /// <inheritdoc/>
    protected override void OnLoopFaulted(Exception exception) =>
        logger.LogError(
            exception,
            "The watch loop for {ResourceType} exited unexpectedly and stopped watching.",
            typeof(TEntity).Name);

    /// <inheritdoc/>
    protected override void DisposeManagedResources() => client.Dispose();

    /// <inheritdoc/>
    protected override ValueTask DisposeManagedResourcesAsync() => CastAndDisposeAsync(client);

    protected virtual async Task OnEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken)
    {
        if (await IsDuplicateAsync(eventType, entity, cancellationToken))
        {
            return;
        }

        var enqueued = await EnqueueEventAsync(eventType, entity, cancellationToken);

        if (!enqueued)
        {
            logger
                .LogTrace(
                    """Enqueue of "{Identifier}" was dropped; leaving deduplication cache unchanged.""",
                    entity.ToIdentifierString());
            return;
        }

        if (eventType == WatchEventType.Deleted)
        {
            await RemoveDedupTokenAsync(entity, cancellationToken);
        }
        else
        {
            await RecordDedupTokenAsync(entity, cancellationToken);
        }
    }

    /// <summary>
    /// Invoked immediately before a watch session (re)connects. <paramref name="isFullRelist"/> is
    /// <see langword="true"/> when the session starts from a null/reset resource version (initial connect,
    /// HTTP 410 Gone, or leader re-acquisition), i.e. the server will replay the full current state.
    /// Overridden by the shared watcher to reset per-pipeline membership on a full relist.
    /// </summary>
    /// <param name="isFullRelist">Whether the upcoming session performs a full relist.</param>
    protected virtual void OnWatchSessionStarting(bool isFullRelist)
    {
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        string? currentVersion = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // A null resource version means the server replays the full current state (initial connect,
                // 410 Gone reset below, or a fresh ExecuteAsync after leader re-acquisition).
                OnWatchSessionStarting(isFullRelist: currentVersion is null);

                await foreach ((WatchEventType type, TEntity entity) in client.WatchAsync<TEntity>(
                                   settings.Namespace,
                                   resourceVersion: currentVersion,
                                   labelSelector: await labelSelector.GetLabelSelectorAsync(cancellationToken),
                                   fieldSelector: await fieldSelector.GetFieldSelectorAsync(cancellationToken),
                                   allowWatchBookmarks: true,
                                   cancellationToken: cancellationToken))
                {
                    using var activity = activitySource.StartActivity($"""processing "{type}" event""", ActivityKind.Consumer);
                    using var scope = logger.BeginScope(scopeFactory.CreateFor(type, entity));

                    metrics?.RecordWatchEvent(typeof(TEntity).Name, type.ToMetricString());

                    logger
                        .LogInformation(
                            """Received watch event "{EventType}" for "{Identifier}", last observed resource version: {ResourceVersion}.""",
                            type,
                            entity.ToIdentifierString(),
                            entity.ResourceVersion());

                    if (type == WatchEventType.Bookmark)
                    {
                        currentVersion = entity.ResourceVersion();
                        continue;
                    }

                    try
                    {
                        await OnEventAsync(type, entity, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        logger
                            .LogError(
                                e,
                                """Scheduling for reconciliation of "{EventType}" for "{Identifier}" failed.""",
                                type,
                                entity.ToIdentifierString());
                    }
                }

                _watcherReconnectRetries = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (KubernetesException e) when (e.Status.Code is (int)HttpStatusCode.Gone)
            {
                logger.LogDebug(e, "Watch restarting with reset bookmark due to 410 HTTP Gone.");

                _watcherReconnectRetries = 0;
                currentVersion = null;
            }
            catch (Exception e)
            {
                await OnWatchErrorAsync(e, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            logger.LogInformation(
                "Watcher for {ResourceType} was terminated and is reconnecting.",
                typeof(TEntity).Name);
        }
    }

    /// <summary>
    /// Enqueues a received (and deduplicated) watch event for reconciliation. The default implementation
    /// enqueues into this watcher's entity queue; a shared watcher (one watch connection serving multiple
    /// controller pipelines) overrides this to dispatch the event to every matching pipeline's queue.
    /// </summary>
    /// <param name="eventType">The watch event type.</param>
    /// <param name="entity">The entity received from the watch stream.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> when the event was scheduled on at least one queue; <see langword="false"/>
    /// when it was dropped (the deduplication cache is then left unchanged).
    /// </returns>
    protected virtual Task<bool> EnqueueEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken) =>
        entityQueue.Enqueue(
            entity,
            eventType.ToReconciliationType(),
            ReconciliationTriggerSource.ApiServer,
            queueIn: TimeSpan.Zero,
            retryCount: 0,
            cancellationToken);

    private static string GetDeletionFingerprint(TEntity entity)
        => string.Join(
            ':',
            "deleting",
            entity.Metadata.DeletionTimestamp?.ToUniversalTime().ToString("O"),
            entity.Metadata.DeletionGracePeriodSeconds,
            entity.Generation(),
            string.Join(',', entity.Finalizers() ?? []));

    // The dedup cache is shared by all watchers of an entity type. When multiple controller pipelines watch
    // the same entity type (each with its own watcher), the partition token isolates their entries so one
    // pipeline's dedup token cannot suppress another pipeline's event for the same object.
    private string GetCacheKey(TEntity entity) =>
        cachePartition.Length == 0 ? entity.Uid() : $"{cachePartition}:{entity.Uid()}";

    private string GetDeletionCacheKey(TEntity entity) => $"{GetCacheKey(entity)}:deletion";

    private async Task<bool> IsDuplicateAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken)
    {
        if (eventType == WatchEventType.Deleted)
        {
            return false;
        }

        var deletionTrackingEntry = entity.Metadata.DeletionTimestamp is not null
            ? new DeletionTrackingEntry(GetDeletionCacheKey(entity), GetDeletionFingerprint(entity))
            : null;

        switch (settings.ReconcileStrategy)
        {
            case ReconcileStrategy.ByGeneration when deletionTrackingEntry is not null:
                var cachedDeletionFingerprint = await EntityCache.TryGetAsync<string>(
                    deletionTrackingEntry.CacheKey,
                    token: cancellationToken);

                if (cachedDeletionFingerprint.HasValue && cachedDeletionFingerprint.Value == deletionTrackingEntry.Fingerprint)
                {
                    logger
                        .LogDebug(
                            """Entity "{Identifier}" deletion state did not change. Skip event.""",
                            entity.ToIdentifierString());
                    return true;
                }

                return false;

            case ReconcileStrategy.ByGeneration when deletionTrackingEntry is null:
                var cachedGeneration = await EntityCache.TryGetAsync<long>(
                    GetCacheKey(entity),
                    token: cancellationToken);

                // skip reconcile if generation did not increase.
                if (cachedGeneration.HasValue && cachedGeneration.Value >= entity.Generation())
                {
                    logger
                        .LogDebug(
                            """Entity "{Identifier}" modification did not modify generation. Skip event.""",
                            entity.ToIdentifierString());
                    return true;
                }

                return false;

            case ReconcileStrategy.ByResourceVersion:
                // reconcile on every change; resourceVersion changes for all mutations, including finalizer removals
                var cachedResourceVersion = await EntityCache.TryGetAsync<string>(
                    GetCacheKey(entity),
                    token: cancellationToken);

                if (cachedResourceVersion.HasValue && cachedResourceVersion.Value == entity.ResourceVersion())
                {
                    logger
                        .LogDebug(
                            """Entity "{Identifier}" resourceVersion unchanged. Skip event.""",
                            entity.ToIdentifierString());
                    return true;
                }

                return false;

            default:
                throw new InvalidOperationException($"Unsupported reconcile strategy: {settings.ReconcileStrategy}.");
        }
    }

    private async Task RecordDedupTokenAsync(TEntity entity, CancellationToken cancellationToken)
    {
        var deletionTrackingEntry = entity.Metadata.DeletionTimestamp is not null
            ? new DeletionTrackingEntry(GetDeletionCacheKey(entity), GetDeletionFingerprint(entity))
            : null;

        switch (settings.ReconcileStrategy)
        {
            case ReconcileStrategy.ByGeneration when deletionTrackingEntry is not null:
                await EntityCache.SetAsync(
                    deletionTrackingEntry.CacheKey,
                    deletionTrackingEntry.Fingerprint,
                    tags: _entityCacheTags,
                    token: cancellationToken);

                break;
            case ReconcileStrategy.ByGeneration when deletionTrackingEntry is null:
                await EntityCache.SetAsync(
                    GetCacheKey(entity),
                    entity.Generation() ?? 1,
                    tags: _entityCacheTags,
                    token: cancellationToken);

                break;
            case ReconcileStrategy.ByResourceVersion:
                await EntityCache.SetAsync(
                    GetCacheKey(entity),
                    entity.ResourceVersion(),
                    tags: _entityCacheTags,
                    token: cancellationToken);

                break;
        }
    }

    private async Task RemoveDedupTokenAsync(TEntity entity, CancellationToken cancellationToken)
    {
        await EntityCache.RemoveAsync(GetCacheKey(entity), token: cancellationToken);
        await EntityCache.RemoveAsync(GetDeletionCacheKey(entity), token: cancellationToken);
    }

    private async Task OnWatchErrorAsync(Exception e, CancellationToken cancellationToken)
    {
        switch (e)
        {
            case SerializationException when
                e.InnerException is JsonException &&
                e.InnerException.Message.Contains("The input does not contain any JSON tokens"):
                logger.LogDebug(
                    """The watcher received an empty response for resource "{Resource}".""",
                    typeof(TEntity));
                return;

            case HttpRequestException when
                e.InnerException is EndOfStreamException &&
                e.InnerException.Message.Contains("Attempted to read past the end of the stream."):
                logger.LogDebug(
                    """The watcher received a known error from the watched resource "{Resource}". This indicates that there are no instances of this resource.""",
                    typeof(TEntity));
                return;
        }

        logger.LogError(e, """There was an error while watching the resource "{Resource}".""", typeof(TEntity));
        _watcherReconnectRetries++;
        metrics?.RecordWatcherReconnection(typeof(TEntity).Name);

        var delay = ExponentialRetryBackoff.GetDelayWithJitter(_watcherReconnectRetries);
        logger.LogWarning(
            "There were {Retries} errors / retries in the watcher. Wait {Seconds}s before next attempt to connect.",
            _watcherReconnectRetries,
            delay.TotalSeconds);

        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Stop or leadership loss during the backoff
        }
    }

    private sealed record DeletionTrackingEntry(string CacheKey, string Fingerprint);
}
