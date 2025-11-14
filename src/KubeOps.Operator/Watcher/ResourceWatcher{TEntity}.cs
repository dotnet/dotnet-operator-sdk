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
using KubeOps.Operator.Logging;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Watcher;

public class ResourceWatcher<TEntity>(
    ActivitySource activitySource,
    ILogger<ResourceWatcher<TEntity>> logger,
    IReconciler<TEntity> reconciler,
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

    protected virtual async Task<ReconciliationResult<TEntity>> OnEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken)
        => await reconciler.Reconcile(
            ReconciliationContext<TEntity>.CreateFromApiServerEvent(entity, eventType),
            cancellationToken);

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
                        var result = await OnEventAsync(type, entity, stoppingToken);

                        if (!result.IsSuccess)
                        {
                            logger.LogError(
                                result.Error,
                                "Reconciliation of {EventType} for {Kind}/{Name} failed with message '{Message}'.",
                                type,
                                entity.Kind,
                                entity.Name(),
                                result.ErrorMessage);
                        }
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
                        logger.LogError(
                            e,
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
}
