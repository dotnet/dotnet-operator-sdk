// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Finalizer;
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
    IServiceProvider serviceProvider,
    OperatorSettings operatorSettings,
    ITimedEntityQueue<TEntity> entityQueue,
    IKubernetesClient client)
    : IReconciler<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly IFusionCache _entityCache = cacheProvider.GetCache(CacheConstants.CacheNames.ResourceWatcher);

    public async Task<ReconciliationResult<TEntity>> Reconcile(ReconciliationContext<TEntity> reconciliationContext, CancellationToken cancellationToken)
    {
        await entityQueue
            .Remove(
                reconciliationContext.Entity,
                cancellationToken);

        var result = reconciliationContext.EventType switch
        {
            WatchEventType.Added or WatchEventType.Modified =>
                await ReconcileModification(reconciliationContext, cancellationToken),
            WatchEventType.Deleted =>
                await ReconcileDeletion(reconciliationContext, cancellationToken),
            _ => throw new NotSupportedException($"Reconciliation event type {reconciliationContext.EventType} is not supported!"),
        };

        if (result.RequeueAfter.HasValue)
        {
            await entityQueue
                .Enqueue(
                    result.Entity,
                    reconciliationContext.EventType.ToRequeueType(),
                    result.RequeueAfter.Value,
                    cancellationToken);
        }

        return result;
    }

    private async Task<ReconciliationResult<TEntity>> ReconcileModification(ReconciliationContext<TEntity> reconciliationContext, CancellationToken cancellationToken)
    {
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

                return await ReconcileEntity(reconciliationContext.Entity, cancellationToken);
            case { Metadata: { DeletionTimestamp: not null, Finalizers.Count: > 0 } }:
                return await ReconcileFinalizersSequential(reconciliationContext.Entity, cancellationToken);
            default:
                return ReconciliationResult<TEntity>.Success(reconciliationContext.Entity);
        }
    }

    private async Task<ReconciliationResult<TEntity>> ReconcileDeletion(ReconciliationContext<TEntity> reconciliationContext, CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var controller = scope.ServiceProvider.GetRequiredService<IEntityController<TEntity>>();
        var result = await controller.DeletedAsync(reconciliationContext.Entity, cancellationToken);

        if (result.IsSuccess)
        {
            await _entityCache.RemoveAsync(reconciliationContext.Entity.Uid(), token: cancellationToken);
        }

        return result;
    }

    private async Task<ReconciliationResult<TEntity>> ReconcileEntity(TEntity entity, CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        if (operatorSettings.AutoAttachFinalizers)
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

    private async Task<ReconciliationResult<TEntity>> ReconcileFinalizersSequential(TEntity entity, CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        // the condition to call ReconcileFinalizersSequentialAsync is:
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

        if (operatorSettings.AutoDetachFinalizers)
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
}
