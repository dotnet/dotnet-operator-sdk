// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
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
        await entityQueue
            .Remove(
                reconciliationContext.Entity,
                cancellationToken);

        await using var scope = serviceProvider.CreateAsyncScope();
        var result = await DispatchToMatchingControllers(
            scope.ServiceProvider,
            reconciliationContext.Entity,
            (ctrl, entity, ct) => ctrl.DeletedAsync(entity, ct),
            cancellationToken);

        if (result.IsSuccess)
        {
            await _entityCache.RemoveAsync(reconciliationContext.Entity.Uid(), token: cancellationToken);
        }

        return result;
    }

    private async Task<ReconciliationResult<TEntity>> ReconcileEntity(TEntity entity, CancellationToken cancellationToken)
    {
        await entityQueue
            .Remove(
                entity,
                cancellationToken);

        await using var scope = serviceProvider.CreateAsyncScope();

        if (operatorSettings.AutoAttachFinalizers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finalizers = scope.ServiceProvider.GetKeyedServices<IEntityFinalizer<TEntity>>(KeyedService.AnyKey);

            var anyFinalizerAdded = false;
            foreach (var finalizer in finalizers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await finalizer.ShouldHandle(entity, cancellationToken))
                {
                    continue;
                }

                anyFinalizerAdded = entity.AddFinalizer(finalizer.GetIdentifierName(entity)) || anyFinalizerAdded;
            }

            if (anyFinalizerAdded)
            {
                entity = await client.UpdateAsync(entity, cancellationToken);
            }
        }

        return await DispatchToMatchingControllers(
            scope.ServiceProvider,
            entity,
            (ctrl, e, ct) => ctrl.ReconcileAsync(e, ct),
            cancellationToken);
    }

    /// <summary>
    /// Resolves all <see cref="IEntityController{TEntity}"/> registrations and, in registration order,
    /// asks each controller <see cref="IEntityController{TEntity}.ShouldHandle"/> against the current
    /// (possibly mutated) entity just-in-time and dispatches <paramref name="operation"/> when it claims
    /// responsibility. On the first failure the chain short-circuits and that failure is returned.
    /// If no controller is registered at all the result is a configuration-error failure; if controllers
    /// are registered but none claim responsibility, a success result is returned and a warning is logged.
    /// <para>
    /// <b>RequeueAfter aggregation:</b> across successful controller results, the earliest non-null
    /// <see cref="ReconciliationResult{TEntity}.RequeueAfter"/> is kept, so an auditing controller that
    /// returns <c>Success(entity)</c> never erases a requeue requested by an earlier controller.
    /// </para>
    /// </summary>
    private async Task<ReconciliationResult<TEntity>> DispatchToMatchingControllers(
        IServiceProvider services,
        TEntity entity,
        Func<IEntityController<TEntity>, TEntity, CancellationToken, Task<ReconciliationResult<TEntity>>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var registeredControllers = services.GetServices<IEntityController<TEntity>>().ToList();
        if (registeredControllers.Count == 0)
        {
            return ReconciliationResult<TEntity>.Failure(
                entity,
                $"No IEntityController<{typeof(TEntity).Name}> registered. Did you forget to call AddController<T, TEntity>() on the operator builder?");
        }

        var currentEntity = entity;
        TimeSpan? aggregatedRequeueAfter = null;
        var anyDispatched = false;

        foreach (var controller in registeredControllers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Evaluate ShouldHandle just-in-time against the (possibly mutated) current entity —
            // so a controller that would claim the *initial* state but reject the *post-mutation*
            // state does not get invoked.
            if (!await controller.ShouldHandle(currentEntity, cancellationToken))
            {
                continue;
            }

            anyDispatched = true;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await operation(controller, currentEntity, cancellationToken);
            if (!result.IsSuccess)
            {
                return result;
            }

            currentEntity = result.Entity;

            if (result.RequeueAfter is not null &&
                (aggregatedRequeueAfter is null || result.RequeueAfter < aggregatedRequeueAfter))
            {
                aggregatedRequeueAfter = result.RequeueAfter;
            }
        }

        if (!anyDispatched)
        {
            logger.LogWarning(
                """No responsible controller found for "{Kind}/{Name}". Skipping.""",
                currentEntity.Kind,
                currentEntity.Name());
            return ReconciliationResult<TEntity>.Success(currentEntity);
        }

        return ReconciliationResult<TEntity>.Success(currentEntity, aggregatedRequeueAfter);
    }

    private async Task<ReconciliationResult<TEntity>> ReconcileFinalizersSequential(TEntity entity, CancellationToken cancellationToken)
    {
        await entityQueue
            .Remove(
                entity,
                cancellationToken);

        await using var scope = serviceProvider.CreateAsyncScope();

        // the condition to call ReconcileFinalizersSequentialAsync is:
        // { Metadata: { DeletionTimestamp: not null, Finalizers.Count: > 0 } }
        // which implies that there is at least a single finalizer
        var identifier = entity.Finalizers()[0];

        if (scope.ServiceProvider.GetKeyedService<IEntityFinalizer<TEntity>>(identifier) is not
            { } finalizer)
        {
            logger.LogInformation(
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

        if (operatorSettings.AutoDetachFinalizers && entity.RemoveFinalizer(identifier))
        {
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
