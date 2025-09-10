// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Finalizer;
using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Constants;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Reconciliation;

internal sealed class Reconciler<TEntity>(
    ILogger<Reconciler<TEntity>> logger,
    IFusionCacheProvider cacheProvider,
    IServiceProvider provider,
    TimedEntityQueue<TEntity> requeue,
    IKubernetesClient client)
    : IReconciler<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly IFusionCache _entityCache = cacheProvider.GetCache(CacheConstants.CacheNames.ResourceWatcher);

    public async Task<Result<TEntity>> ReconcileCreation(TEntity entity, CancellationToken cancellationToken)
    {
        requeue.Remove(entity);
        var cachedGeneration = await _entityCache.TryGetAsync<long?>(entity.Uid(), token: cancellationToken);

        if (!cachedGeneration.HasValue)
        {
            // Only perform reconciliation if the entity was not already in the cache.
            var result = await ReconcileModificationAsync(entity, cancellationToken);

            if (result.IsSuccess)
            {
                await _entityCache.SetAsync(entity.Uid(), entity.Generation() ?? 0, token: cancellationToken);
            }

            if (result.RequeueAfter.HasValue)
            {
                requeue.Enqueue(
                    result.Entity,
                    result.IsSuccess ? RequeueType.Modified : RequeueType.Added,
                    result.RequeueAfter.Value);
            }

            return result;
        }

        logger.LogDebug(
            """Received ADDED event for entity "{Kind}/{Name}" which was already in the cache. Skip event.""",
            entity.Kind,
            entity.Name());

        return Result<TEntity>.ForSuccess(entity);
    }

    public async Task<Result<TEntity>> ReconcileModification(TEntity entity, CancellationToken cancellationToken)
    {
        requeue.Remove(entity);

        Result<TEntity> result;

        switch (entity)
        {
            case { Metadata.DeletionTimestamp: null }:
                var cachedGeneration = await _entityCache.TryGetAsync<long?>(entity.Uid(), token: cancellationToken);

                // Check if entity spec has changed through "Generation" value increment. Skip reconcile if not changed.
                if (cachedGeneration.HasValue && cachedGeneration >= entity.Generation())
                {
                    logger.LogDebug(
                        """Entity "{Kind}/{Name}" modification did not modify generation. Skip event.""",
                        entity.Kind,
                        entity.Name());

                    return Result<TEntity>.ForSuccess(entity);
                }

                // update cached generation since generation now changed
                await _entityCache.SetAsync(entity.Uid(), entity.Generation() ?? 1, token: cancellationToken);
                result = await ReconcileModificationAsync(entity, cancellationToken);

                break;
            case { Metadata: { DeletionTimestamp: not null, Finalizers.Count: > 0 } }:
                result = await ReconcileFinalizersSequentialAsync(entity, cancellationToken);

                break;
            default:
                result = Result<TEntity>.ForSuccess(entity);

                break;
        }

        if (result.RequeueAfter.HasValue)
        {
            requeue.Enqueue(result.Entity, RequeueType.Modified, result.RequeueAfter.Value);
        }

        return result;
    }

    public async Task<Result<TEntity>> ReconcileDeletion(TEntity entity, CancellationToken cancellationToken)
    {
        requeue.Remove(entity);

        await using var scope = provider.CreateAsyncScope();
        var controller = scope.ServiceProvider.GetRequiredService<IEntityController<TEntity>>();
        var result = await controller.DeletedAsync(entity, cancellationToken);

        if (result.IsSuccess)
        {
            await _entityCache.RemoveAsync(entity.Uid(), token: cancellationToken);
        }

        if (result.RequeueAfter.HasValue)
        {
            requeue.Enqueue(
                result.Entity,
                RequeueType.Deleted,
                result.RequeueAfter.Value);
        }

        return result;
    }

    private async Task<Result<TEntity>> ReconcileFinalizersSequentialAsync(TEntity entity, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();

        // condition to call ReconcileFinalizersSequentialAsync is:
        // { Metadata: { DeletionTimestamp: not null, Finalizers.Count: > 0 } }
        // which implies that there is at least a single finalizer
        var identifier = entity.Finalizers()[0];

        if (scope.ServiceProvider.GetKeyedService<IEntityFinalizer<TEntity>>(identifier) is not
            { } finalizer)
        {
            logger.LogDebug(
                """Entity "{Kind}/{Name}" is finalizing but this operator has no registered finalizers for the identifier {FinalizerIdentifier}.""",
                entity.Kind,
                entity.Name(),
                identifier);
            return Result<TEntity>.ForSuccess(entity);
        }

        var result = await finalizer.FinalizeAsync(entity, cancellationToken);

        if (!result.IsSuccess)
        {
            return result;
        }

        entity = result.Entity;
        entity.RemoveFinalizer(identifier);
        entity = await client.UpdateAsync(entity, cancellationToken);

        logger.LogInformation(
            """Entity "{Kind}/{Name}" finalized with "{Finalizer}".""",
            entity.Kind,
            entity.Name(),
            identifier);

        return Result<TEntity>.ForSuccess(entity, result.RequeueAfter);
    }

    private async Task<Result<TEntity>> ReconcileModificationAsync(TEntity entity, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var controller = scope.ServiceProvider.GetRequiredService<IEntityController<TEntity>>();
        return await controller.ReconcileAsync(entity, cancellationToken);
    }
}
