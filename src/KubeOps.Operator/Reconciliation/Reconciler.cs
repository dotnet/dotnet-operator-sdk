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
using KubeOps.Operator.Logging;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    IServiceProvider serviceProvider,
    OperatorSettings operatorSettings,
    ITimedEntityQueue<TEntity> entityQueue,
    IKubernetesClient client)
    : IReconciler<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    public async Task<ReconciliationResult<TEntity>> Reconcile(ReconciliationContext<TEntity> reconciliationContext, CancellationToken cancellationToken)
    {
        var result = reconciliationContext.EventType switch
        {
            ReconciliationType.Added or ReconciliationType.Modified =>
                reconciliationContext.Entity switch
                {
                    { Metadata.DeletionTimestamp: null }
                        => await ReconcileEntity(reconciliationContext.Entity, cancellationToken),
                    { Metadata: { DeletionTimestamp: not null, Finalizers.Count: > 0 } }
                        => await ReconcileFinalizersSequential(reconciliationContext.Entity, cancellationToken),
                    _ => ReconciliationResult<TEntity>.Success(reconciliationContext.Entity),
                },
            ReconciliationType.Deleted =>
                await ReconcileDeletion(reconciliationContext, cancellationToken),
            _ => throw new NotSupportedException($"Reconciliation event type {reconciliationContext.EventType} is not supported!"),
        };

        if (result.RequeueAfter.HasValue)
        {
            await entityQueue
                .Enqueue(
                    result.Entity,
                    reconciliationContext.EventType,
                    ReconciliationTriggerSource.Operator,
                    result.RequeueAfter.Value,
                    cancellationToken);
        }

        return result;
    }

    private async Task<ReconciliationResult<TEntity>> ReconcileDeletion(ReconciliationContext<TEntity> reconciliationContext, CancellationToken cancellationToken)
    {
        await entityQueue
            .Remove(
                reconciliationContext.Entity,
                cancellationToken);

        await using var scope = serviceProvider.CreateAsyncScope();
        var controller = scope.ServiceProvider.GetRequiredService<IEntityController<TEntity>>();
        return await controller.DeletedAsync(reconciliationContext.Entity, cancellationToken);
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
            var finalizers = scope.ServiceProvider.GetKeyedServices<IEntityFinalizer<TEntity>>(KeyedService.AnyKey);

            var anyFinalizerAdded = finalizers
                .Aggregate(
                    false,
                    (changed, finalizer) => entity.AddFinalizer(finalizer.GetIdentifierName(entity)) || changed);

            if (anyFinalizerAdded)
            {
                entity = await client.UpdateAsync(entity, cancellationToken);
            }
        }

        var controller = scope.ServiceProvider.GetRequiredService<IEntityController<TEntity>>();
        return await controller.ReconcileAsync(entity, cancellationToken);
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
            logger
                .LogInformation(
                    """Entity "{Identifier}" is finalizing but this operator has no registered finalizers for the identifier "{FinalizerIdentifier}".""",
                    entity.ToIdentifierString(),
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
            // FinalizeAsync completed successfully – removing the finalizer entry from the entity
            // is a commit step that must not be cancelled. If the operator shuts down between
            // FinalizeAsync and this UpdateAsync, the entity would be stuck in finalizer-limbo
            // and could never be deleted by Kubernetes until the operator restarts and re-runs
            // the full finalization again (which may or may not be idempotent in user code).
            entity = await client.UpdateAsync(entity, CancellationToken.None);
        }

        logger
            .LogInformation(
                """Entity "{Identifier}" finalized with "{FinalizerIdentifier}".""",
                entity.ToIdentifierString(),
                identifier);

        return ReconciliationResult<TEntity>.Success(entity, result.RequeueAfter);
    }
}
