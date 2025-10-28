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
using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Finalizer;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Constants;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Watcher;

public class ResourceWatcher<TEntity>(
    ActivitySource activitySource,
    ILogger<ResourceWatcher<TEntity>> logger,
    IServiceProvider provider,
    TimedEntityQueue<TEntity> requeue,
    OperatorSettings settings,
    IEntityLabelSelector<TEntity> labelSelector,
    IFusionCacheProvider cacheProvider,
    IKubernetesClient client)
    : IHostedService, IAsyncDisposable, IDisposable
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly IFusionCache _entityCache = cacheProvider.GetCache(CacheConstants.CacheNames.ResourceWatcher);
    private CancellationTokenSource _cancellationTokenSource = new();
    private uint _watcherReconnectRetries;
    private Task? _eventWatcher;
    private bool _disposed;

    ~ResourceWatcher() => Dispose(false);

    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting resource watcher for {ResourceType}.", typeof(TEntity).Name);

        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
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
        requeue.Dispose();
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
        await CastAndDispose(requeue);
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

    protected virtual async Task OnEventAsync(WatchEventType type, TEntity entity, CancellationToken cancellationToken)
    {
        MaybeValue<long?> cachedGeneration;

        // Make sure Finalizers are running if Termination has began.
        if (type != WatchEventType.Deleted && entity.Metadata.DeletionTimestamp is not null && entity.Metadata.Finalizers.Count > 0)
        {
            await ReconcileFinalizersSequentialAsync(entity, cancellationToken);
            return;
        }

        switch (type)
        {
            case WatchEventType.Added:
                cachedGeneration = await _entityCache.TryGetAsync<long?>(entity.Uid(), token: cancellationToken);

                if (!cachedGeneration.HasValue)
                {
                    // Only perform reconciliation if the entity was not already in the cache.
                    await _entityCache.SetAsync(entity.Uid(), entity.Generation() ?? 0, token: cancellationToken);
                    await ReconcileModificationAsync(entity, cancellationToken);
                }
                else
                {
                    logger.LogDebug(
                        """Received ADDED event for entity "{Kind}/{Name}" which was already in the cache. Skip event.""",
                        entity.Kind,
                        entity.Name());
                }

                break;
            case WatchEventType.Modified:
                cachedGeneration = await _entityCache.TryGetAsync<long?>(entity.Uid(), token: cancellationToken);

                // Check if entity spec has changed through "Generation" value increment. Skip reconcile if not changed.
                if (cachedGeneration.HasValue && cachedGeneration >= entity.Generation())
                {
                    logger.LogDebug(
                        """Entity "{Kind}/{Name}" modification did not modify generation. Skip event.""",
                        entity.Kind,
                        entity.Name());
                    return;
                }

                // update cached generation since generation now changed
                await _entityCache.SetAsync(entity.Uid(), entity.Generation() ?? 1, token: cancellationToken);
                await ReconcileModificationAsync(entity, cancellationToken);

                break;
            case WatchEventType.Deleted:
                await ReconcileDeletionAsync(entity, cancellationToken);
                break;
            default:
                logger.LogWarning(
                    """Received unsupported event "{EventType}" for "{Kind}/{Name}".""",
                    type,
                    entity.Kind,
                    entity.Name());
                break;
        }
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
#pragma warning disable SA1312
                    using var __ = activitySource.StartActivity($"""processing "{type}" event""", ActivityKind.Consumer);
                    using var _ = logger.BeginScope(EntityLoggingScope.CreateFor(type, entity));
                    logger.LogInformation(
                        """Received watch event "{EventType}" for "{Kind}/{Name}", last observed resource version: {ResourceVersion}.""",
                        type,
                        entity.Kind,
                        entity.Name(),
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
                    catch (KubernetesException e) when (e.Status.Code is (int)HttpStatusCode.GatewayTimeout)
                    {
                        logger.LogDebug(e, "Watch restarting due to 504 Gateway Timeout.");
                        break;
                    }
                    catch (KubernetesException e) when (e.Status.Code is (int)HttpStatusCode.Gone)
                    {
                        // Special handling when our resource version is outdated.
                        throw;
                    }
                    catch (Exception e)
                    {
                        LogReconciliationFailed(e);
                    }

                    void LogReconciliationFailed(Exception exception)
                    {
                        logger.LogError(
                            exception,
                            "Reconciliation of {EventType} for {Kind}/{Name} failed.",
                            type,
                            entity.Kind,
                            entity.Name());
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Don't throw if the cancellation was indeed requested.
                break;
            }
            catch (KubernetesException e) when (e.Status.Code is (int)HttpStatusCode.Gone)
            {
                logger.LogDebug(e, "Watch restarting with reset bookmark due to 410 HTTP Gone.");
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
            .Add(TimeSpan.FromMilliseconds(new Random().Next(0, 1000)));
        logger.LogWarning(
            "There were {Retries} errors / retries in the watcher. Wait {Seconds}s before next attempt to connect.",
            _watcherReconnectRetries,
            delay.TotalSeconds);
        await Task.Delay(delay);
    }

    private async Task ReconcileDeletionAsync(TEntity entity, CancellationToken cancellationToken)
    {
        requeue.Remove(entity);
        await _entityCache.RemoveAsync(entity.Uid(), token: cancellationToken);

        await using var scope = provider.CreateAsyncScope();
        var controller = scope.ServiceProvider.GetRequiredService<IEntityController<TEntity>>();
        await controller.DeletedAsync(entity, cancellationToken);
    }

    private async Task ReconcileFinalizersSequentialAsync(TEntity entity, CancellationToken cancellationToken)
    {
        requeue.Remove(entity);
        await using var scope = provider.CreateAsyncScope();

        var identifier = entity.Finalizers().FirstOrDefault();
        if (identifier is null)
        {
            return;
        }

        if (scope.ServiceProvider.GetKeyedService<IEntityFinalizer<TEntity>>(identifier) is not
            { } finalizer)
        {
            logger.LogDebug(
                """Entity "{Kind}/{Name}" is finalizing but this operator has no registered finalizers for the identifier {FinalizerIdentifier}.""",
                entity.Kind,
                entity.Name(),
                identifier);
            return;
        }

        await finalizer.FinalizeAsync(entity, cancellationToken);
        entity.RemoveFinalizer(identifier);
        await client.UpdateAsync(entity, cancellationToken);
        logger.LogInformation(
            """Entity "{Kind}/{Name}" finalized with "{Finalizer}".""",
            entity.Kind,
            entity.Name(),
            identifier);
    }

    private async Task ReconcileModificationAsync(TEntity entity, CancellationToken cancellationToken)
    {
        // Re-queue should requested in the controller reconcile method. Invalidate any existing queues.
        requeue.Remove(entity);
        await using var scope = provider.CreateAsyncScope();
        var controller = scope.ServiceProvider.GetRequiredService<IEntityController<TEntity>>();
        await controller.ReconcileAsync(entity, cancellationToken);
    }
}
