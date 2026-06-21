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
    OperatorMetrics? metrics = null)
    : RestartableHostedService
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private uint _watcherReconnectRetries;

    ~ResourceWatcher() => Dispose(false);

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
    /// <see cref="LeaderAwareResourceWatcher{TEntity}"/> calls <see cref="IFusionCache.Clear"/>
    /// when leadership is lost to ensure stale data is not carried over to the next watch session.
    /// </para>
    /// <para>
    /// Note: when an entity has a <c>DeletionTimestamp</c> set (finalizer processing), the
    /// <see cref="ReconcileStrategy.ByGeneration"/> path bypasses the cache check and does
    /// not update the cached token. The cache therefore reflects the last value observed
    /// before deletion began, not the current state.
    /// </para>
    /// </remarks>
    protected IFusionCache EntityCache { get; } = cacheProvider.GetCache(
        settings.ReconcileStrategy == ReconcileStrategy.ByResourceVersion
            ? CacheConstants.CacheNames.ResourceWatcherByResourceVersion
            : CacheConstants.CacheNames.ResourceWatcher);

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
        var deletionTrackingEntry = entity.Metadata.DeletionTimestamp is not null
            ? new DeletionTrackingEntry(GetDeletionCacheKey(entity), GetDeletionFingerprint(entity))
            : null;

        if (eventType != WatchEventType.Deleted)
        {
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

                        return;
                    }

                    break;
                case ReconcileStrategy.ByGeneration when deletionTrackingEntry is null:
                    var cachedGeneration = await EntityCache.TryGetAsync<long>(
                        entity.Uid(),
                        token: cancellationToken);

                    // skip reconcile if generation did not increase.
                    if (cachedGeneration.HasValue && cachedGeneration.Value >= entity.Generation())
                    {
                        logger
                            .LogDebug(
                                """Entity "{Identifier}" modification did not modify generation. Skip event.""",
                                entity.ToIdentifierString());

                        return;
                    }

                    break;
                case ReconcileStrategy.ByResourceVersion:
                    // reconcile on every change; resourceVersion changes for all mutations, including finalizer removals
                    var cachedResourceVersion = await EntityCache.TryGetAsync<string>(
                        entity.Uid(),
                        token: cancellationToken);

                    if (cachedResourceVersion.HasValue && cachedResourceVersion.Value == entity.ResourceVersion())
                    {
                        logger
                            .LogDebug(
                                """Entity "{Identifier}" resourceVersion unchanged. Skip event.""",
                                entity.ToIdentifierString());

                        return;
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Unsupported reconcile strategy: {settings.ReconcileStrategy}.");
            }
        }

        var enqueued = await entityQueue
            .Enqueue(
                entity,
                eventType.ToReconciliationType(),
                ReconciliationTriggerSource.ApiServer,
                queueIn: TimeSpan.Zero,
                retryCount: 0,
                cancellationToken);

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
            await EntityCache.RemoveAsync(entity.Uid(), token: cancellationToken);
            await EntityCache.RemoveAsync(GetDeletionCacheKey(entity), token: cancellationToken);
        }
        else
        {
            switch (settings.ReconcileStrategy)
            {
                case ReconcileStrategy.ByGeneration when deletionTrackingEntry is not null:
                    await EntityCache.SetAsync(
                        deletionTrackingEntry.CacheKey,
                        deletionTrackingEntry.Fingerprint,
                        token: cancellationToken);

                    break;
                case ReconcileStrategy.ByGeneration when deletionTrackingEntry is null:
                    await EntityCache.SetAsync(
                        entity.Uid(),
                        entity.Generation() ?? 1,
                        token: cancellationToken);

                    break;
                case ReconcileStrategy.ByResourceVersion:
                    await EntityCache.SetAsync(
                        entity.Uid(),
                        entity.ResourceVersion(),
                        token: cancellationToken);

                    break;
            }
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        string? currentVersion = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach ((WatchEventType type, TEntity entity) in client.WatchAsync<TEntity>(
                                   settings.Namespace,
                                   resourceVersion: currentVersion,
                                   labelSelector: await labelSelector.GetLabelSelectorAsync(cancellationToken),
                                   fieldSelector: await fieldSelector.GetFieldSelectorAsync(cancellationToken),
                                   allowWatchBookmarks: true,
                                   cancellationToken: cancellationToken))
                {
                    using var activity = activitySource.StartActivity($"""processing "{type}" event""", ActivityKind.Consumer);
                    using var scope = logger.BeginScope(EntityLoggingScope.CreateFor(type, entity));

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
                await OnWatchErrorAsync(e);
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

    private static string GetDeletionCacheKey(TEntity entity)
        => $"{entity.Uid()}:deletion";

    private static string GetDeletionFingerprint(TEntity entity)
        => string.Join(
            ':',
            "deleting",
            entity.Metadata.DeletionTimestamp?.ToUniversalTime().ToString("O"),
            entity.Metadata.DeletionGracePeriodSeconds,
            entity.Generation(),
            string.Join(',', entity.Finalizers() ?? []));

    private async Task OnWatchErrorAsync(Exception e)
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
        await Task.Delay(delay);
    }

    private sealed record DeletionTrackingEntry(string CacheKey, string Fingerprint);
}
