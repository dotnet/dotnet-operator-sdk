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
using KubeOps.Operator.Queue;
using KubeOps.Operator.Reconciliation;

using Microsoft.Extensions.Hosting;
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
    IKubernetesClient client)
    : IHostedService, IAsyncDisposable, IDisposable
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private CancellationTokenSource _cancellationTokenSource = new();
    private uint _watcherReconnectRetries;
    private Task? _eventWatcher;
    private bool _disposed;

    ~ResourceWatcher() => Dispose(false);

    /// <summary>
    /// Gets the fusion cache used to track the last observed generation for each entity,
    /// enabling generation-based deduplication of watch events.
    /// </summary>
    /// <value>
    /// The <see cref="IFusionCache"/> instance scoped to the resource watcher cache name.
    /// </value>
    /// <remarks>
    /// Subclasses may access this cache to read or invalidate cached generation values.
    /// For example, <see cref="LeaderAwareResourceWatcher{TEntity}"/> calls
    /// <see cref="IFusionCache.Clear"/> when leadership is lost to ensure stale generation
    /// data is not carried over to the next watch session.
    /// </remarks>
    protected IFusionCache EntityCache { get; } = cacheProvider.GetCache(
        settings.ReconcileStrategy == ReconcileStrategy.ByResourceVersion
            ? CacheConstants.CacheNames.ResourceWatcherByResourceVersion
            : CacheConstants.CacheNames.ResourceWatcher);

    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting resource watcher for {ResourceType}.", typeof(TEntity).Name);

        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = new();
        }

        _eventWatcher = WatchClientEventsAsync(_cancellationTokenSource.Token);

        logger.LogInformation("Started resource watcher for {ResourceType}.", typeof(TEntity).Name);
        return Task.CompletedTask;
    }

    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping resource watcher for {ResourceType}.", typeof(TEntity).Name);
        if (_disposed)
        {
            return;
        }

        await _cancellationTokenSource.CancelAsync();
        if (_eventWatcher is not null)
        {
            await _eventWatcher.WaitAsync(cancellationToken);
        }

        logger.LogInformation("Stopped resource watcher for {ResourceType}.", typeof(TEntity).Name);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _cancellationTokenSource.Dispose();
        _eventWatcher?.Dispose();
        client.Dispose();

        _disposed = true;
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_eventWatcher is not null)
        {
            await CastAndDispose(_eventWatcher);
        }

        await CastAndDispose(_cancellationTokenSource);
        await CastAndDispose(client);

        _disposed = true;

        return;

        static async ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
            {
                await resourceAsyncDisposable.DisposeAsync();
            }
            else
            {
                resource.Dispose();
            }
        }
    }

    protected virtual async Task OnEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken)
    {
        if (eventType == WatchEventType.Deleted)
        {
            await EntityCache.RemoveAsync(entity.Uid(), token: cancellationToken);
        }
        else if (settings.ReconcileStrategy == ReconcileStrategy.ByGeneration)
        {
            // bypass generation check for finalizer handling
            // removal does not increase the generation
            if (entity.Metadata.DeletionTimestamp is null)
            {
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

                await EntityCache.SetAsync(
                    entity.Uid(),
                    entity.Generation() ?? 1,
                    token: cancellationToken);
            }
        }
        else
        {
            // ByResourceVersion: reconcile on every write; resourceVersion changes for all mutations
            // including finalizer removals, so no DeletionTimestamp bypass is required
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

            await EntityCache.SetAsync(
                entity.Uid(),
                entity.ResourceVersion() ?? string.Empty,
                token: cancellationToken);
        }

        // queue entity for reconciliation
        await entityQueue
            .Enqueue(
                entity,
                eventType.ToReconciliationType(),
                ReconciliationTriggerSource.ApiServer,
                queueIn: TimeSpan.Zero,
                retryCount: 0,
                cancellationToken);
    }

    private async Task WatchClientEventsAsync(CancellationToken stoppingToken)
    {
        string? currentVersion = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach ((WatchEventType type, TEntity entity) in client.WatchAsync<TEntity>(
                                   settings.Namespace,
                                   resourceVersion: currentVersion,
                                   labelSelector: await labelSelector.GetLabelSelectorAsync(stoppingToken),
                                   allowWatchBookmarks: true,
                                   cancellationToken: stoppingToken))
                {
                    using var activity = activitySource.StartActivity($"""processing "{type}" event""", ActivityKind.Consumer);
                    using var scope = logger.BeginScope(EntityLoggingScope.CreateFor(type, entity));

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
                        await OnEventAsync(type, entity, stoppingToken);
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            logger.LogInformation(
                "Watcher for {ResourceType} was terminated and is reconnecting.",
                typeof(TEntity).Name);
        }
    }

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

        var delay = TimeSpan
            .FromSeconds(Math.Pow(2, Math.Clamp(_watcherReconnectRetries, 0, 5)))
            .Add(TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)));
        logger.LogWarning(
            "There were {Retries} errors / retries in the watcher. Wait {Seconds}s before next attempt to connect.",
            _watcherReconnectRetries,
            delay.TotalSeconds);
        await Task.Delay(delay);
    }
}
