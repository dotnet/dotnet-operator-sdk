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
/// Global semaphore (<c>_parallelismSemaphore</c>) limits the total number of concurrent reconciliations
/// based on <see cref="ParallelReconciliationOptions.MaxParallelReconciliations"/>. This semaphore is acquired
/// <strong>before</strong> reading from the queue, implementing true back-pressure to prevent unbounded memory growth.
/// </description>
/// </item>
/// <item>
/// <description>
/// Per-entity UID locks (<c>_uidEntries</c>) ensure that only one reconciliation per entity can run at a time,
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
/// This ensures that the queue consumption rate matches the processing capacity, preventing memory leaks from
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
    private readonly ConcurrentDictionary<string, UidEntry> _uidEntries = new();
    private readonly SemaphoreSlim _parallelismSemaphore = new(
        operatorSettings.ParallelReconciliationOptions.MaxParallelReconciliations,
        operatorSettings.ParallelReconciliationOptions.MaxParallelReconciliations);

    private bool _disposed;

    /// <inheritdoc cref="IHostedService.StartAsync"/>
    /// <remarks>
    /// Schedules the queue processing loop as a background task using <see cref="Task.Run(Func{Task}, CancellationToken)"/>.
    /// The <paramref name="cancellationToken"/> passed to this method is intentionally not used for the processing loop;
    /// cancellation is managed via an internal <see cref="CancellationTokenSource"/> that is signaled by <see cref="StopAsync"/>.
    /// This avoids cancelling the scheduled work during the host startup phase.
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The current implementation of IHostedService expects that StartAsync is "really" asynchronous.
        // Blocking calls are not allowed, they would stop the rest of the startup flow.
        //
        // This has been an open issue since 2019 and is not expected to be closed soon. (https://github.com/dotnet/runtime/issues/36063)
        // For reasons unknown at the time of writing this code, "await Task.Yield()" didn't work as expected, it caused
        // a deadlock in 1/10 of the cases.
        //
        // Therefore, we use Task.Run() and put the work to queue. The passed cancellation token of the StartAsync
        // method is not used because it would only cancel the scheduling (which we definitely don't want to cancel).
        // To make this intention explicit, CancellationToken.None gets passed.
        _ = Task.Run(() => WatchAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
        => _disposed
            ? Task.CompletedTask
            : _cts.CancelAsync();

    /// <inheritdoc/>
    public void Dispose()
    {
        _cts.Dispose();
        _parallelismSemaphore.Dispose();

        foreach (var entry in _uidEntries.Values)
        {
            entry.Semaphore.Dispose();
        }

        _uidEntries.Clear();
        client.Dispose();
        queue.Dispose();

        _disposed = true;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await CastAndDispose(_cts);
        await CastAndDispose(_parallelismSemaphore);

        foreach (var entry in _uidEntries.Values)
        {
            await CastAndDispose(entry.Semaphore);
        }

        _uidEntries.Clear();
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

        var entity = entry.ReconciliationType == ReconciliationType.Deleted
            ? entry.Entity
            : await client.GetAsync<TEntity>(entry.Entity.Name(), entry.Entity.Namespace(), cancellationToken);

        if (entity is not null)
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown, no action needed.
        }
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
        var uidEntry = _uidEntries.GetOrAdd(uid, _ => new UidEntry(new SemaphoreSlim(1, 1)));

        // Increment access count at the start.
        // This ensures the lock will not be disposed while any task is still accessing it.
        Interlocked.Increment(ref uidEntry.AccessCount);

        using var activity = activitySource.StartActivity($"""Processing queued "{entry.ReconciliationType}" event for "{entry.Entity.ToIdentifierString()}".""", ActivityKind.Consumer);
        using var scope = logger.BeginScope(EntityLoggingScope.CreateFor(entry.ReconciliationType, entry.ReconciliationTriggerSource, entry.Entity));

        try
        {
            var canAcquireLock = operatorSettings.ParallelReconciliationOptions.ConflictStrategy switch
            {
                ParallelReconciliationConflictStrategy.Discard => await uidEntry.Semaphore.WaitAsync(0, cancellationToken),
                ParallelReconciliationConflictStrategy.RequeueAfterDelay => await uidEntry.Semaphore.WaitAsync(0, cancellationToken),
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
                await uidEntry.Semaphore.WaitAsync(cancellationToken);
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
                uidEntry.Semaphore.Release();
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

            // Retry with exponential back-off up to the configured limit to handle transient failures
            // (e.g. temporary network outages). Once the limit is reached, the entry is dropped so that
            // a non-transient error cannot cause an infinite retry loop.
            // See: https://github.com/dotnet/dotnet-operator-sdk/issues/554
            var nextRetryCount = entry.RetryCount + 1;
            var maxRetries = operatorSettings.ParallelReconciliationOptions.MaxErrorRetries;

            if (maxRetries > 0 && nextRetryCount <= maxRetries)
            {
                var delay = operatorSettings.ParallelReconciliationOptions.GetErrorBackoffDelay(nextRetryCount);
                logger.LogWarning(
                    """Requeueing "{Identifier}" for error-retry {RetryCount}/{MaxRetries} in {Delay}s.""",
                    entry.Entity.ToIdentifierString(),
                    nextRetryCount,
                    maxRetries,
                    delay.TotalSeconds);

                // The original trigger source is preserved so the reconciler always knows
                // what event originally caused this reconciliation (e.g. ApiServer or Operator).
                // RetryCount is incremented and passed explicitly so the back-off calculation
                // stays correct across successive failures without losing state.
                await queue.Enqueue(
                    entry.Entity,
                    entry.ReconciliationType,
                    entry.ReconciliationTriggerSource,
                    delay,
                    nextRetryCount,
                    CancellationToken.None);
            }
            else
            {
                logger.LogError(
                    """Entity "{Identifier}" has exceeded the maximum error-retry limit of {MaxRetries}. Dropping entry.""",
                    entry.Entity.ToIdentifierString(),
                    maxRetries);
            }
        }
        finally
        {
            // Decrement the access count. When it reaches 0, all tasks for this UID have completed,
            // so it's safe to remove and dispose the lock.
            if (Interlocked.Decrement(ref uidEntry.AccessCount) == 0
                && uidEntry.Semaphore.CurrentCount is 1
                && _uidEntries.TryRemove(uid, out var removedEntry))
            {
                removedEntry.Semaphore.Dispose();
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
                    retryCount: 0,
                    cancellationToken);
                break;

            default:
                throw new NotSupportedException($"Conflict strategy {operatorSettings.ParallelReconciliationOptions.ConflictStrategy} is not supported in HandleUidConflictAsync.");
        }
    }

    private sealed record UidEntry(SemaphoreSlim Semaphore)
    {
#pragma warning disable SA1401 – Interlocked requires ref access
        public int AccessCount;
#pragma warning restore SA1401
    }
}
