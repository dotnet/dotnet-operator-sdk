// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Constants;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Reconciliation;

/// <summary>
/// The Reconciler class provides mechanisms for handling creation, modification, and deletion
/// events for Kubernetes objects of the specified entity type. It implements the IReconciler
/// interface and facilitates the reconciliation of desired and actual states of the entity.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes entity being reconciled. Must implement IKubernetesObject
/// with V1ObjectMeta.
/// </typeparam>
/// <remarks>
/// This class leverages logging, caching, and client services to manage and process
/// Kubernetes objects effectively. It also uses internal queuing capabilities for entity
/// processing and requeuing.
/// </remarks>
internal sealed class Reconciler<TEntity>(
    ILogger<Reconciler<TEntity>> logger,
    IFusionCacheProvider cacheProvider,
    IServiceProvider provider,
    OperatorSettings settings,
    ITimedEntityQueue<TEntity> requeue,
    IKubernetesClient client)
    : IReconciler<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly IFusionCache _entityCache = cacheProvider.GetCache(CacheConstants.CacheNames.ResourceWatcher);

    public async Task<ReconciliationResult<TEntity>> ReconcileCreation(ReconciliationContext<TEntity> reconciliationContext, CancellationToken cancellationToken)
    {
        await requeue.Remove(reconciliationContext.Entity);

        if (reconciliationContext.Entity.Metadata.DeletionTimestamp is not null)
        {
            logger.LogDebug(
                """Received ADDED event for entity "{Kind}/{Name}" which already has a deletion timestamp "{DeletionTimestamp}". Skip event.""",
                reconciliationContext.Entity.Kind,
                reconciliationContext.Entity.Name(),
                reconciliationContext.Entity.Metadata.DeletionTimestamp.Value.ToString("O"));

            return ReconciliationResult<TEntity>.Success(reconciliationContext.Entity);
        }

        if (reconciliationContext.IsTriggeredByApiServer())
        {
            var cachedGeneration =
                await _entityCache.TryGetAsync<long?>(reconciliationContext.Entity.Uid(), token: cancellationToken);

            // Only perform reconciliation if the entity was not already in the cache.
            if (cachedGeneration.HasValue)
            {
                logger.LogDebug(
                    """Received ADDED event for entity "{Kind}/{Name}" which was already in the cache. Skip event.""",
                    reconciliationContext.Entity.Kind,
                    reconciliationContext.Entity.Name());

                return ReconciliationResult<TEntity>.Success(reconciliationContext.Entity);
            }

            await _entityCache.SetAsync(
                reconciliationContext.Entity.Uid(),
                reconciliationContext.Entity.Generation() ?? 0,
                token: cancellationToken);
        }

        var result = await ReconcileModificationAsync(reconciliationContext.Entity, cancellationToken);

        if (result.RequeueAfter.HasValue)
        {
            await requeue.Enqueue(
                result.Entity,
                result.IsSuccess ? RequeueType.Modified : RequeueType.Added,
                result.RequeueAfter.Value);
        }

        return result;
    }

    public async Task<ReconciliationResult<TEntity>> ReconcileModification(ReconciliationContext<TEntity> reconciliationContext, CancellationToken cancellationToken)
    {
        await requeue.Remove(reconciliationContext.Entity);

        ReconciliationResult<TEntity> reconciliationResult;

        switch (reconciliationContext.Entity)
        {
            case { Metadata.DeletionTimestamp: null }:
                if (reconciliationContext.IsTriggeredByApiServer())
                {
                    var cachedGeneration = await _entityCache.TryGetAsync<long?>(
                        reconciliationContext.Entity.Uid(),
                        token: cancellationToken);

                    // Check if entity-spec has changed through "Generation" value increment. Skip reconcile if not changed.
                    if (cachedGeneration.HasValue && cachedGeneration >= reconciliationContext.Entity.Generation())
                    {
                        logger.LogDebug(
                            """Entity "{Kind}/{Name}" modification did not modify generation. Skip event.""",
                            reconciliationContext.Entity.Kind,
                            reconciliationContext.Entity.Name());

                        return ReconciliationResult<TEntity>.Success(reconciliationContext.Entity);
                    }

                    // update cached generation since generation now changed
                    await _entityCache.SetAsync(
                        reconciliationContext.Entity.Uid(),
                        reconciliationContext.Entity.Generation() ?? 1,
                        token: cancellationToken);
                }

                reconciliationResult = await ReconcileModificationAsync(reconciliationContext.Entity, cancellationToken);

                break;
            case { Metadata: { DeletionTimestamp: not null, Finalizers.Count: > 0 } }:
                reconciliationResult = await ReconcileFinalizersSequentialAsync(reconciliationContext.Entity, cancellationToken);

                break;
            default:
                reconciliationResult = ReconciliationResult<TEntity>.Success(reconciliationContext.Entity);

                break;
        }

        if (reconciliationResult.RequeueAfter.HasValue)
        {
            await requeue
                .Enqueue(
                    reconciliationResult.Entity,
                    RequeueType.Modified,
                    reconciliationResult.RequeueAfter.Value);
        }

        return reconciliationResult;
    }

    public async Task<ReconciliationResult<TEntity>> ReconcileDeletion(ReconciliationContext<TEntity> reconciliationContext, CancellationToken cancellationToken)
    {
        await requeue.Remove(reconciliationContext.Entity);

        await using var scope = provider.CreateAsyncScope();
        var controller = scope.ServiceProvider.GetRequiredService<IEntityController<TEntity>>();
        var result = await controller.DeletedAsync(reconciliationContext.Entity, cancellationToken);

        if (result.IsSuccess)
        {
            await _entityCache.RemoveAsync(reconciliationContext.Entity.Uid(), token: cancellationToken);
        }

        if (result.RequeueAfter.HasValue)
        {
            await requeue
                .Enqueue(
                    result.Entity,
                    RequeueType.Deleted,
                    result.RequeueAfter.Value);
        }

        return result;
    }

    private async Task<ReconciliationResult<TEntity>> ReconcileFinalizersSequentialAsync(TEntity entity, CancellationToken cancellationToken)
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
            return ReconciliationResult<TEntity>.Success(entity);
        }

        var result = await finalizer.FinalizeAsync(entity, cancellationToken);

        if (!result.IsSuccess)
        {
            return result;
        }

        entity = result.Entity;

        if (settings.AutoDetachFinalizers)
        {
            entity.RemoveFinalizer(identifier);
            entity = await client.UpdateAsync(entity, cancellationToken);
        }

        logger.LogInformation(
            """Entity "{Kind}/{Name}" finalized with "{Finalizer}".""",
            entity.Kind,
            entity.Name(),
            identifier);

        return ReconciliationResult<TEntity>.Success(entity, result.RequeueAfter);
    }

    private async Task<ReconciliationResult<TEntity>> ReconcileModificationAsync(TEntity entity, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();

        if (settings.AutoAttachFinalizers)
        {
            var finalizers = scope.ServiceProvider.GetKeyedServices<IEntityFinalizer<TEntity>>(KeyedService.AnyKey);

            foreach (var finalizer in finalizers)
            {
                entity.AddFinalizer(finalizer.GetIdentifierName(entity));
            }

            entity = await client.UpdateAsync(entity, cancellationToken);
        }

        var controller = scope.ServiceProvider.GetRequiredService<IEntityController<TEntity>>();
        return await controller.ReconcileAsync(entity, cancellationToken);
    }
}
