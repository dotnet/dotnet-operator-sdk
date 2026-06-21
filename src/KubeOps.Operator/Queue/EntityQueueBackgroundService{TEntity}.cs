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
using KubeOps.Operator.Metrics;

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
/// based on <see cref="ParallelReconciliationSettings.MaxParallelReconciliations"/>. This semaphore is acquired
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
public class EntityQueueBackgroundService<TEntity>(
    ActivitySource activitySource,
    IKubernetesClient client,
    OperatorSettings operatorSettings,
    ITimedEntityQueue<TEntity> queue,
    IReconciler<TEntity> reconciler,
    ILogger<EntityQueueBackgroundService<TEntity>> logger,
    OperatorMetrics? metrics = null) : IHostedService, IDisposable, IAsyncDisposable, IEntityQueueConsumer<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly ConcurrentDictionary<string, UidEntry> _uidEntries = new();
    private readonly SemaphoreSlim _parallelismSemaphore = new(
        operatorSettings.ParallelReconciliation.MaxParallelReconciliations,
        operatorSettings.ParallelReconciliation.MaxParallelReconciliations);

    // Guards the start/stop lifecycle: _running (idempotency gate for the current term) and _activeRuns.
    private readonly object _lifecycleLock = new();

    // Every processing loop that has been started and not yet finished, with the token source it owns. There is
    // normally one, but a leadership flap (StoppedLeading -> StartedLeading) can briefly leave the previous loop
    // still draining its in-flight reconciliations while the next loop is already running. Dispose drains them
    // ALL, so no worker can touch a semaphore/client/queue after it was disposed.
    private readonly List<(Task Loop, CancellationTokenSource Cts)> _activeRuns = [];

    private bool _running;
    private volatile bool _disposed;

    /// <summary>
    /// Bounds how long a stop/dispose waits for in-flight reconciliations to drain. A non-cooperative reconciler
    /// that ignores its <see cref="CancellationToken"/> cannot block shutdown beyond this. Internal so tests can
    /// shorten it.
    /// </summary>
    internal TimeSpan DrainGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the timed entity queue this service consumes. Exposed for leadership-aware subclasses that need
    /// to suspend intake or clear the queue on a leadership transition.
    /// </summary>
    protected ITimedEntityQueue<TEntity> Queue => queue;

    /// <inheritdoc cref="IHostedService.StartAsync"/>
    /// <remarks>
    /// Schedules the queue processing loop as a background task using <see cref="Task.Run(Func{Task}, CancellationToken)"/>.
    /// The <paramref name="cancellationToken"/> passed to this method is intentionally not used for the processing loop;
    /// cancellation is managed via an internal <see cref="CancellationTokenSource"/> that is signaled by <see cref="StopAsync"/>.
    /// This avoids cancelling the scheduled work during the host startup phase.
    /// </remarks>
    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            // Idempotent: if a loop is already running, do not start a second one. This closes the race where
            // the leadership-aware StartAsync IsLeader() branch and a concurrent OnStartedLeading callback both
            // call base.StartAsync — two loops would reconcile every entry twice in parallel.
            if (_disposed || _running)
            {
                return Task.CompletedTask;
            }

            // Fresh token source for this run, owned and disposed by that run's loop (see
            // RunProcessingLoopAsync). A flap restart therefore never disposes a token source a still-running
            // former loop is still observing through the queue enumerator.
            var cts = new CancellationTokenSource();
            _running = true;

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
            var loop = Task.Run(() => RunProcessingLoopAsync(cts), CancellationToken.None);
            _activeRuns.Add((loop, cts));

            return Task.CompletedTask;
        }
    }

    /// <inheritdoc/>
    public virtual Task StopAsync(CancellationToken cancellationToken)
    {
        (Task Loop, CancellationTokenSource Cts)[] runs;
        lock (_lifecycleLock)
        {
            if (_disposed || !_running)
            {
                return Task.CompletedTask;
            }

            // Clear the desired-running state so a subsequent StartAsync (e.g. on re-acquired leadership) starts
            // a fresh loop instead of being suppressed by the idempotency guard.
            _running = false;
            runs = [.. _activeRuns];
        }

        // Stop must not block on the drain (the OnStoppedLeading callback fire-and-forgets it): only request
        // cancellation. Each loop drains its own workers (see WatchAsync) and DisposeAsyncCore awaits them all
        // before tearing down shared resources.
        return CancelRunsAsync(runs);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the resources used by the background service.
    /// </summary>
    /// <param name="disposing">Whether the method is called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        // The synchronous path cannot await the loops to drain (see DisposeAsyncCore for the draining path).
        // The container disposes via IAsyncDisposable when available, so this is the best-effort fallback.
        _parallelismSemaphore.Dispose();

        lock (_uidEntries)
        {
            foreach (var entry in _uidEntries.Values)
            {
                entry.Semaphore.Dispose();
            }

            _uidEntries.Clear();
        }

        client.Dispose();
        queue.Dispose();

        _disposed = true;
    }

    /// <summary>
    /// Asynchronously releases the resources used by the background service.
    /// </summary>
    /// <remarks>
    /// Overriding subclasses must call <c>base.DisposeAsyncCore()</c> so the shared resources are released on
    /// the asynchronous disposal path too. This mirrors <see cref="Dispose(bool)"/>: the dependency injection
    /// container disposes via <see cref="IAsyncDisposable"/> when available, so subclass cleanup that only
    /// hooks <see cref="Dispose(bool)"/> would otherwise be skipped.
    /// </remarks>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        // Stop and drain EVERY processing loop ever started — a leadership flap can leave the previous loop
        // still draining its in-flight reconciliations while a new loop runs — before tearing down shared
        // resources, so no still-running reconciliation can touch an already-disposed semaphore, client or
        // queue. Each loop disposes its own token source once it finishes. Cancellation is cooperative: a
        // reconciler that ignores its token can delay this up to DrainGracePeriod.
        (Task Loop, CancellationTokenSource Cts)[] runs;
        lock (_lifecycleLock)
        {
            _running = false;
            runs = [.. _activeRuns];
        }

        await DrainRunsAsync(runs, CancellationToken.None);

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

    protected virtual async Task<ReconciliationResult<TEntity>> ReconcileSingleAsync(QueueEntry<TEntity> entry, CancellationToken cancellationToken)
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

    private static async Task CancelRunsAsync(IReadOnlyCollection<(Task Loop, CancellationTokenSource Cts)> runs)
    {
        foreach (var (_, cts) in runs)
        {
            try
            {
                await cts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // The loop already finished and disposed its own token source; nothing to cancel.
            }
        }
    }

    private async Task DrainRunsAsync(
        IReadOnlyCollection<(Task Loop, CancellationTokenSource Cts)> runs, CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return;
        }

        await CancelRunsAsync(runs);

        try
        {
            // Bound the wait so a non-cooperative reconciler that ignores cancellation cannot block shutdown
            // indefinitely; after the grace elapses we proceed (documented limitation).
            await Task.WhenAll(runs.Select(r => r.Loop)).WaitAsync(DrainGracePeriod, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Grace elapsed while a reconciliation was still running; proceed with disposal.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown deadline reached; proceed.
        }
    }

    // Runs one processing loop and owns its CancellationTokenSource: it disposes the source only after the
    // loop has finished, so StartAsync never disposes a token source that this loop is still using.
    private async Task RunProcessingLoopAsync(CancellationTokenSource cts)
    {
        try
        {
            await WatchAsync(cts.Token);
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _activeRuns.RemoveAll(r => ReferenceEquals(r.Cts, cts));
            }

            cts.Dispose();
        }
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>(operatorSettings.ParallelReconciliation.MaxParallelReconciliations);

        try
        {
            await foreach (var queueEntry in queue.WithCancellation(cancellationToken))
            {
                await _parallelismSemaphore.WaitAsync(cancellationToken);

                var task = ProcessEntryWithSemaphoreReleaseAsync(queueEntry, cancellationToken);
                tasks.Add(task);

                // Periodic cleanup of completed tasks
                if (tasks.Count >= operatorSettings.ParallelReconciliation.MaxParallelReconciliations)
                {
                    tasks.RemoveAll(t => t.IsCompleted);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown / leadership loss, no action needed.
        }
        finally
        {
            // Drain the worker tasks already spawned so the loop does not return — and its CancellationTokenSource
            // is not disposed (see RunProcessingLoopAsync), nor shared resources torn down, nor a new leadership
            // term started — while reconciliations are still in flight. Individual worker failures are already
            // handled inside ProcessEntryAsync; cancellation surfaces here as OperationCanceledException.
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Workers cancelled as part of the stop; their outcomes are already handled in ProcessEntryAsync.
            }
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
        UidEntry uidEntry;
        lock (_uidEntries)
        {
            uidEntry = _uidEntries.GetOrAdd(uid, _ => new(new(1, 1)));
            uidEntry.AccessCount++;
        }

        using var activity = activitySource.StartActivity($"""Processing queued "{entry.ReconciliationType}" event for "{entry.Entity.ToIdentifierString()}".""", ActivityKind.Consumer);
        using var scope = logger.BeginScope(EntityLoggingScope.CreateFor(entry.ReconciliationType, entry.ReconciliationTriggerSource, entry.Entity));

        try
        {
            var canAcquireLock = operatorSettings.ParallelReconciliation.ConflictStrategy switch
            {
                ParallelReconciliationConflictStrategy.Discard or ParallelReconciliationConflictStrategy.RequeueAfterDelay => await uidEntry.Semaphore.WaitAsync(0, cancellationToken),
                ParallelReconciliationConflictStrategy.WaitForCompletion => true,
                _ => throw new NotSupportedException($"Conflict strategy {operatorSettings.ParallelReconciliation.ConflictStrategy} is not supported."),
            };

            if (!canAcquireLock)
            {
                await HandleLockingConflictAsync(entry, cancellationToken);
                return;
            }

            if (operatorSettings.ParallelReconciliation.ConflictStrategy is ParallelReconciliationConflictStrategy.WaitForCompletion)
            {
                logger
                    .LogDebug(
                        """Trying to acquire lock for "{Identifier}". Waiting for completion.""",
                        entry.Entity.ToIdentifierString());
                await uidEntry.Semaphore.WaitAsync(cancellationToken);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                logger
                    .LogInformation(
                        """Starting reconciliation for "{Identifier}".""",
                        entry.Entity.ToIdentifierString());

                var result = await ReconcileSingleAsync(entry, cancellationToken);

                metrics?.RecordReconciliation(
                    typeof(TEntity).Name,
                    entry.ReconciliationType.ToMetricString(),
                    result.IsSuccess ? "success" : "failure",
                    stopwatch.Elapsed.TotalSeconds,
                    result.IsSuccess ? null : result.Error?.GetType().FullName ?? "_OTHER");

                logger
                    .LogInformation(
                        """Completed reconciliation for "{Identifier}" {State}.""",
                        entry.Entity.ToIdentifierString(),
                        result.IsSuccess
                            ? "successfully"
                            : "with failures");
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                metrics?.RecordReconciliation(
                    typeof(TEntity).Name,
                    entry.ReconciliationType.ToMetricString(),
                    "failure",
                    stopwatch.Elapsed.TotalSeconds,
                    e.GetType().FullName);
                throw;
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
            var maxRetries = operatorSettings.ParallelReconciliation.MaxErrorRetries;

            if (maxRetries > 0 && nextRetryCount <= maxRetries)
            {
                var delay = operatorSettings.ParallelReconciliation.GetErrorBackoffDelay(nextRetryCount);
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
                var scheduled = await queue.Enqueue(
                    entry.Entity,
                    entry.ReconciliationType,
                    entry.ReconciliationTriggerSource,
                    delay,
                    nextRetryCount,
                    cancellationToken);

                // Only count the retry when it was actually scheduled. A leadership-aware queue with
                // suspended intake (leadership just lost) drops the entry and returns false.
                if (scheduled)
                {
                    metrics?.RecordRequeue(typeof(TEntity).Name, "error_retry");
                }
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
            lock (_uidEntries)
            {
                if (--uidEntry.AccessCount == 0)
                {
                    _uidEntries.TryRemove(uid, out _);
                }
            }
        }
    }

    private async Task HandleLockingConflictAsync(QueueEntry<TEntity> entry, CancellationToken cancellationToken)
    {
        switch (operatorSettings.ParallelReconciliation.ConflictStrategy)
        {
            case ParallelReconciliationConflictStrategy.Discard:
                logger
                    .LogDebug(
                        """Entity "{Identifier}" is already being reconciled. Discarding request.""",
                        entry.Entity.ToIdentifierString());
                metrics?.RecordDiscard(typeof(TEntity).Name);
                break;

            case ParallelReconciliationConflictStrategy.RequeueAfterDelay:
                var requeueDelay = operatorSettings.ParallelReconciliation.GetEffectiveRequeueDelay();

                logger.LogDebug(
                    """Entity "{Identifier}" is already being reconciled. Requeueing after {Delay}s.""",
                    entry.Entity.ToIdentifierString(),
                    requeueDelay.TotalSeconds);

                var scheduled = await queue.Enqueue(
                    entry.Entity,
                    entry.ReconciliationType,
                    entry.ReconciliationTriggerSource,
                    requeueDelay,
                    retryCount: 0,
                    cancellationToken);

                // Only count the requeue when it was actually scheduled (a suspended gate drops it).
                if (scheduled)
                {
                    metrics?.RecordRequeue(typeof(TEntity).Name, "conflict");
                }

                break;

            default:
                throw new NotSupportedException($"Conflict strategy {operatorSettings.ParallelReconciliation.ConflictStrategy} is not supported in HandleUidConflictAsync.");
        }
    }

    private sealed record UidEntry(SemaphoreSlim Semaphore)
    {
        public int AccessCount { get; set; }
    }
}
