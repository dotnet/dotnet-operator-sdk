// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Logging;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

/// <summary>
/// A background service responsible for managing the queue mechanism of Kubernetes entities.
/// It processes entities from a timed queue and invokes the reconciliation logic for each entity.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes entity being managed. This entity must implement the <see cref="IKubernetesObject{V1ObjectMeta}"/> interface.
/// </typeparam>
/// <remarks>
/// <para>
/// This service implements a two-level locking strategy to control parallelism and prevent concurrent modifications:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// A global semaphore (<c>_parallelismSemaphore</c>) limits the total number of concurrent reconciliations
/// based on <see cref="ParallelReconciliationOptions.MaxParallelReconciliations"/>. This semaphore is acquired
/// <strong>before</strong> reading from the queue, implementing true back-pressure to prevent unbounded memory growth.
/// </description>
/// </item>
/// <item>
/// <description>
/// Per-entity UID locks (<c>_uidLocks</c>) ensure that only one reconciliation per entity can run at a time,
/// preventing concurrent modifications to the same entity. Each entity's UID gets its own <see cref="SemaphoreSlim"/> instance.
/// </description>
/// </item>
/// </list>
/// <para>
/// When a conflict is detected (an entity is already being reconciled), the behavior is determined by
/// <see cref="ParallelReconciliationConflictStrategy"/>:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ParallelReconciliationConflictStrategy.Discard"/> - The reconciliation request is discarded.</description></item>
/// <item><description><see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/> - The entity is requeued with a delay.</description></item>
/// <item><description><see cref="ParallelReconciliationConflictStrategy.WaitForCompletion"/> - The request waits for the current reconciliation to complete.</description></item>
/// </list>
/// <para>
/// The service implements back-pressure by acquiring the parallelism semaphore before reading from the queue.
/// This ensures that queue consumption rate matches the processing capacity, preventing memory leaks from
/// unbounded task accumulation.
/// </para>
/// </remarks>
internal sealed class EntityQueueBackgroundService<TEntity>(
    ActivitySource activitySource,
    IKubernetesClient client,
    OperatorSettings operatorSettings,
    ITimedEntityQueue<TEntity> queue,
    IReconciler<TEntity> reconciler,
    ILogger<EntityQueueBackgroundService<TEntity>> logger) : IHostedService, IDisposable, IAsyncDisposable
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _uidLocks = new();
    private readonly SemaphoreSlim _parallelismSemaphore = new(
        operatorSettings.ParallelReconciliationOptions.MaxParallelReconciliations,
        operatorSettings.ParallelReconciliationOptions.MaxParallelReconciliations);

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
        => _disposed
            ? Task.CompletedTask
            : _cts.CancelAsync();

    public void Dispose()
    {
        _cts.Dispose();
        _parallelismSemaphore.Dispose();

        foreach (var lockItem in _uidLocks.Values)
        {
            lockItem.Dispose();
        }

        _uidLocks.Clear();
        client.Dispose();
        queue.Dispose();

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await CastAndDispose(_cts);
        await CastAndDispose(_parallelismSemaphore);

        foreach (var lockItem in _uidLocks.Values)
        {
            await CastAndDispose(lockItem);
        }

        _uidLocks.Clear();
        await CastAndDispose(client);
        await CastAndDispose(queue);

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

    private async Task<ReconciliationResult<TEntity>> ReconcileSingleAsync(QueueEntry<TEntity> entry, CancellationToken cancellationToken)
    {
        logger
            .LogTrace(
                """Executing requested queued reconciliation for "{Identifier}".""",
                entry.Entity.ToIdentifierString());

        if (await client.GetAsync<TEntity>(entry.Entity.Name(), entry.Entity.Namespace(), cancellationToken) is { } entity)
        {
            return await reconciler.Reconcile(
                ReconciliationContext<TEntity>.CreateFor(
                    entity,
                    entry.ReconciliationType,
                    entry.ReconciliationTriggerSource),
                cancellationToken);
        }

        logger
            .LogWarning(
                """Queued entity "{Identifier}" was not found. Skipping reconciliation.""",
                entry.Entity.ToIdentifierString());
        return ReconciliationResult<TEntity>.Failure(entry.Entity, "Entity was not found.");
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>(operatorSettings.ParallelReconciliationOptions.MaxParallelReconciliations);

        try
        {
            await foreach (var queueEntry in queue.WithCancellation(cancellationToken))
            {
                await _parallelismSemaphore.WaitAsync(cancellationToken);

                var task = ProcessEntryWithSemaphoreReleaseAsync(queueEntry, cancellationToken);
                tasks.Add(task);

                // Periodic cleanup of completed tasks
                if (tasks.Count >= operatorSettings.ParallelReconciliationOptions.MaxParallelReconciliations)
                {
                    tasks.RemoveAll(t => t.IsCompleted);
                }
            }

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(ex, "Queue processing cancelled during shutdown.");
        }
#pragma warning disable CA2254, S2139
        catch (Exception ex)
        {
            // Log and rethrow is intentional: we want to log the error for diagnostics
            // AND signal the hosting infrastructure that the service has failed critically
            logger.LogCritical(ex, "Fatal error in queue processing. Service will stop.");
            throw;
        }
#pragma warning restore CA2254, S2139
    }

    /// <summary>
    /// Processes a queue entry and ensures the parallelism semaphore is released afterwards.
    /// </summary>
    /// <param name="entry">The queue entry to process.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <remarks>
    /// This method assumes the parallelism semaphore has already been acquired before calling.
    /// </remarks>
    private async Task ProcessEntryWithSemaphoreReleaseAsync(QueueEntry<TEntity> entry, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessEntryAsync(entry, cancellationToken);
        }
        finally
        {
            _parallelismSemaphore.Release();
        }
    }

    private async Task ProcessEntryAsync(QueueEntry<TEntity> entry, CancellationToken cancellationToken)
    {
        var uid = entry.Entity.Uid();
        var uidLock = _uidLocks.GetOrAdd(uid, _ => new(1, 1));

        using var activity = activitySource.StartActivity($"""Processing queued "{entry.ReconciliationType}" event for "{entry.Entity.ToIdentifierString()}".""", ActivityKind.Consumer);
        using var scope = logger.BeginScope(EntityLoggingScope.CreateFor(entry.ReconciliationType, entry.Entity));

        try
        {
            var canAcquireLock = operatorSettings.ParallelReconciliationOptions.ConflictStrategy switch
            {
                ParallelReconciliationConflictStrategy.Discard => await uidLock.WaitAsync(0, cancellationToken),
                ParallelReconciliationConflictStrategy.RequeueAfterDelay => await uidLock.WaitAsync(0, cancellationToken),
                ParallelReconciliationConflictStrategy.WaitForCompletion => true,
                _ => throw new NotSupportedException($"Conflict strategy {operatorSettings.ParallelReconciliationOptions.ConflictStrategy} is not supported."),
            };

            if (!canAcquireLock)
            {
                await HandleLockingConflictAsync(entry, cancellationToken);
                return;
            }

            if (operatorSettings.ParallelReconciliationOptions.ConflictStrategy is ParallelReconciliationConflictStrategy.WaitForCompletion)
            {
                logger
                    .LogDebug(
                        """Trying to acquire lock for "{Identifier}". Waiting for completion.""",
                        entry.Entity.ToIdentifierString());
                await uidLock.WaitAsync(cancellationToken);
            }

            try
            {
                logger
                    .LogInformation(
                        """Starting reconciliation for "{Identifier}".""",
                        entry.Entity.ToIdentifierString());

                var result = await ReconcileSingleAsync(entry, cancellationToken);

                logger
                    .LogInformation(
                        """Completed reconciliation for "{Identifier}" {State}.""",
                        entry.Entity.ToIdentifierString(),
                        result.IsSuccess
                            ? "successfully"
                            : "with failures");
            }
            finally
            {
                uidLock.Release();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Catches all unexpected errors, including OperationCanceledException that was NOT triggered
            // by the operator's own cancellation token (i.e. an internal abort from within the reconciler).
            // Intentional shutdown cancellations (IsCancellationRequested == true) are re-thrown and handled
            // by the caller in WatchAsync.
            logger
                .LogError(
                    e,
                    """Queued "{ReconciliationType}" reconciliation for "{Identifier}" failed.""",
                    entry.ReconciliationType,
                    entry.Entity.ToIdentifierString());
        }
        finally
        {
            if (uidLock.CurrentCount is 1 && _uidLocks.TryRemove(uid, out var removedLock))
            {
                removedLock.Dispose();
            }
        }
    }

    private async Task HandleLockingConflictAsync(QueueEntry<TEntity> entry, CancellationToken cancellationToken)
    {
        switch (operatorSettings.ParallelReconciliationOptions.ConflictStrategy)
        {
            case ParallelReconciliationConflictStrategy.Discard:
                logger
                    .LogDebug(
                        """Entity "{Identifier}" is already being reconciled. Discarding request.""",
                        entry.Entity.ToIdentifierString());
                break;

            case ParallelReconciliationConflictStrategy.RequeueAfterDelay:
                logger.LogDebug(
                    """Entity "{Identifier}" is already being reconciled. Requeueing after {Delay}s.""",
                    entry.Entity.ToIdentifierString(),
                    operatorSettings.ParallelReconciliationOptions.GetEffectiveRequeueDelay().TotalSeconds);

                await queue.Enqueue(
                    entry.Entity,
                    entry.ReconciliationType,
                    entry.ReconciliationTriggerSource,
                    operatorSettings.ParallelReconciliationOptions.GetEffectiveRequeueDelay(),
                    cancellationToken);
                break;

            default:
                throw new NotSupportedException($"Conflict strategy {operatorSettings.ParallelReconciliationOptions.ConflictStrategy} is not supported in HandleUidConflictAsync.");
        }
    }
}
