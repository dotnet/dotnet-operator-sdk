// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Reconciliation;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

internal sealed class EntityRequeueBackgroundService<TEntity>(
    IKubernetesClient client,
    TimedEntityQueue<TEntity> queue,
    IReconciler<TEntity> reconciler,
    ILogger<EntityRequeueBackgroundService<TEntity>> logger) : IHostedService, IDisposable, IAsyncDisposable
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The current implementation of IHostedService expects that StartAsync is "really" asynchronous.
        // Blocking calls are not allowed, they would stop the rest of the startup flow.
        //
        // This is an open issue since 2019 and not expected to be closed soon. (https://github.com/dotnet/runtime/issues/36063)
        // For reasons unknown at the time of writing this code, "await Task.Yield()" didn't work as expected, it caused
        // a deadlock in 1/10 of the cases.
        //
        // Therefore, we use Task.Run() and put the work to queue. The passed cancellation token of the StartAsync
        // method is not used, because it would only cancel the scheduling (which we definitely don't want to cancel).
        // To make this intention explicit, CancellationToken.None gets passed.
        _ = Task.Run(() => WatchAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        return _cts.CancelAsync();
    }

    public void Dispose()
    {
        _cts.Dispose();
        client.Dispose();
        queue.Dispose();

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await CastAndDispose(_cts);
        await CastAndDispose(client);
        await CastAndDispose(queue);

        _disposed = true;

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

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        await foreach (var entry in queue)
        {
            try
            {
                await ReconcileSingleAsync(entry, cancellationToken);
            }
            catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    e,
                    """Queued reconciliation for the entity of type {ResourceType} for "{Kind}/{Name}" failed.""",
                    typeof(TEntity).Name,
                    entry.Entity.Kind,
                    entry.Entity.Name());
            }
            catch (Exception e)
            {
                logger.LogError(
                    e,
                    """Queued reconciliation for the entity of type {ResourceType} for "{Kind}/{Name}" failed.""",
                    typeof(TEntity).Name,
                    entry.Entity.Kind,
                    entry.Entity.Name());
            }
        }
    }

    private async Task ReconcileSingleAsync(RequeueEntry<TEntity> queuedEntry, CancellationToken cancellationToken)
    {
        logger.LogTrace("""Execute requested requeued reconciliation for "{Name}".""", queuedEntry.Entity.Name());

        if (await client.GetAsync<TEntity>(queuedEntry.Entity.Name(), queuedEntry.Entity.Namespace(), cancellationToken) is not
            { } entity)
        {
            logger.LogWarning(
                """Requeued entity "{Name}" was not found. Skipping reconciliation.""", queuedEntry.Entity.Name());
            return;
        }

        switch (queuedEntry.RequeueType)
        {
            case RequeueType.Added:
                await reconciler.ReconcileCreation(entity, cancellationToken);
                break;
            case RequeueType.Modified:
                await reconciler.ReconcileModification(entity, cancellationToken);
                break;
            case RequeueType.Deleted:
                await reconciler.ReconcileDeletion(entity, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"RequeueType '{queuedEntry.RequeueType}' is not supported!");
        }
    }
}
