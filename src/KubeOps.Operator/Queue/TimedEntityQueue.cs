// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Metrics;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Represents a queue that's used to inspect a Kubernetes entity again after a given time.
/// The given enumerable only contains items that should be considered for reconciliations.
/// </summary>
/// <typeparam name="TEntity">The type of the inner entity.</typeparam>
/// <remarks>
/// <para>
/// This implementation uses a <see cref="PeriodicTimer"/> to periodically check for entries
/// that are ready to be reconciled, instead of creating individual tasks for each scheduled entry.
/// This approach significantly reduces memory overhead when many entities are scheduled with long delays.
/// </para>
/// <para>
/// The timer checks for ready entries every 100 milliseconds, providing a good balance between
/// responsiveness and CPU usage. Entries scheduled with very short delays (less than the timer interval)
/// may experience slightly longer actual delays than requested, but this is typically acceptable
/// for reconciliation operations.
/// </para>
/// </remarks>
public sealed class TimedEntityQueue<TEntity> : ITimedEntityQueue<TEntity>, ISuspendableEntityQueue
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// The interval at which the timer checks for entries ready to be added to the queue.
    /// </summary>
    private const int TimerIntervalMilliseconds = 100;

    private readonly ILogger<TimedEntityQueue<TEntity>> _logger;
    private readonly OperatorMetrics? _metrics;

    // Used for managing all scheduled entries that should be added to the queue in the future.
    private readonly ConcurrentDictionary<string, TimedQueueEntry<TEntity>> _management = new();

    // The actual queue containing all the entries that have to be reconciled.
    private readonly BlockingCollection<QueueEntry<TEntity>> _queue = new(new ConcurrentQueue<QueueEntry<TEntity>>());

    // Periodic timer that checks for ready entries.
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(TimerIntervalMilliseconds));

    // Cancellation token source for the timer loop.
    private readonly CancellationTokenSource _timerCts = new();

    // Guards the intake gate together with all mutations of _management/_queue that must be atomic with the
    // gate check (Enqueue scheduling, Clear, and timer promotion). Without this, a leadership transition has
    // a TOCTOU race: Enqueue passes the gate check, SuspendIntake + Clear run, and Enqueue then re-adds an
    // entry after the clear.
    private readonly object _gateLock = new();

    // Task that runs the timer loop.
    private readonly Task _timerTask;

    // Set while the instance does not hold leadership; guarded by _gateLock. When true, Enqueue drops new
    // entries and the timer promotes nothing.
    private bool _intakeSuspended;

    // Read by the meter's observation thread (queue-depth gauge) and written on the dispose thread,
    // hence volatile to ensure the disposed state is observed promptly across threads.
    private volatile bool _disposed;

    public TimedEntityQueue(ILogger<TimedEntityQueue<TEntity>> logger, OperatorMetrics? metrics = null)
    {
        _logger = logger;
        _metrics = metrics;
        _timerTask = Task.Run(ProcessScheduledEntriesAsync);

        // The gauge callbacks are invoked by the (long-lived) meter for its whole lifetime, which
        // outlives this queue. After Dispose() the BlockingCollection would throw
        // ObjectDisposedException on Count, so the callbacks short-circuit once disposed.
        _metrics?.RegisterQueueDepthGauge(
            typeof(TEntity).Name,
            () => _disposed ? 0 : _management.Count,
            () => _disposed ? 0 : _queue.Count);
    }

    internal int Count => _management.Count;

    // Number of entries already promoted to the ready queue.
    internal int ReadyCount => _queue.Count;

    /// <inheritdoc cref="ITimedEntityQueue{TEntity}.Enqueue"/>
    public Task<bool> Enqueue(TEntity entity, ReconciliationType type, ReconciliationTriggerSource reconciliationTriggerSource, TimeSpan queueIn, int retryCount, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        var key = this.GetKey(entity) ?? throw new InvalidOperationException("Cannot enqueue entities without name.");

        // The gate check and the scheduling mutation must be atomic with SuspendIntake/Clear (see _gateLock),
        // otherwise a former leader could re-add an entry right after the queue was cleared on leadership loss.
        lock (_gateLock)
        {
            if (_intakeSuspended)
            {
                _logger
                    .LogTrace(
                        """Intake suspended for {Entity}; dropping enqueue of "{Identifier}" (trigger {Trigger}).""",
                        typeof(TEntity).Name,
                        entity.ToIdentifierString(),
                        reconciliationTriggerSource.ToMetricString());
                return Task.FromResult(false);
            }

            _management
                .AddOrUpdate(
                    key: key,
                    addValueFactory: _ =>
                    {
                        _logger
                            .LogTrace(
                                """Scheduling entity "{Identifier}" to reconcile in {Seconds}s.""",
                                entity.ToIdentifierString(),
                                queueIn.TotalSeconds);

                        return new(entity, type, reconciliationTriggerSource, queueIn, retryCount);
                    },
                    updateValueFactory: (_, oldEntry) =>
                    {
                        var newQueueIn = queueIn;
                        var oldQueueIn = TimeSpan.FromTicks(
                            Math.Max(0, oldEntry.EnqueueAt.Subtract(DateTimeOffset.UtcNow).Ticks));

                        // the earliest execution time should be kept,
                        if (oldQueueIn <= newQueueIn)
                        {
                            newQueueIn = oldQueueIn;

                            _logger
                                .LogTrace(
                                    """Keeping scheduled entity "{Identifier}" to reconcile in {Seconds}s.""",
                                    entity.ToIdentifierString(),
                                    newQueueIn.TotalSeconds);
                        }
                        else
                        {
                            _logger
                                .LogTrace(
                                    """Updating scheduled entity "{Identifier}" to reconcile in {Seconds}s.""",
                                    entity.ToIdentifierString(),
                                    newQueueIn.TotalSeconds);
                        }

                        // schedule deleted reconciliations must not be cancelled
                        var newReconciliationType = oldEntry.ReconciliationType == ReconciliationType.Deleted
                            ? oldEntry.ReconciliationType
                            : type;

                        oldEntry.Cancel();
                        return new(entity, newReconciliationType, reconciliationTriggerSource, newQueueIn, retryCount);
                    });
        }

        _metrics?.RecordEnqueue(typeof(TEntity).Name, reconciliationTriggerSource.ToMetricString());

        return Task.FromResult(true);
    }

    /// <inheritdoc cref="ISuspendableEntityQueue.Clear"/>
    public void Clear()
    {
        // Drain both collections atomically with the intake gate, but never CompleteAdding() the
        // BlockingCollection — the queue must stay usable after ResumeIntake() once leadership is regained.
        lock (_gateLock)
        {
            var scheduled = _management.Count;
            _management.Clear();

            var ready = 0;
            while (_queue.TryTake(out _))
            {
                // Discard the ready entry; draining only, never CompleteAdding().
                ready++;
            }

            _logger
                .LogDebug(
                    "Cleared entity queue for {Entity} on leadership loss: discarded {Scheduled} scheduled and {Ready} ready entries.",
                    typeof(TEntity).Name,
                    scheduled,
                    ready);
        }
    }

    /// <inheritdoc cref="ISuspendableEntityQueue.SuspendIntake"/>
    public void SuspendIntake()
    {
        lock (_gateLock)
        {
            _intakeSuspended = true;
        }

        _logger
            .LogTrace(
                "Intake gate for {Entity} suspended; new and scheduled entries are dropped until resumed.",
                typeof(TEntity).Name);
    }

    /// <inheritdoc cref="ISuspendableEntityQueue.ResumeIntake"/>
    public void ResumeIntake()
    {
        lock (_gateLock)
        {
            _intakeSuspended = false;
        }

        _logger
            .LogTrace(
                "Intake gate for {Entity} resumed; accepting new entries again.",
                typeof(TEntity).Name);
    }

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop the timer loop by cancelling the token
        _timerCts.Cancel();

        // Wait for timer task to complete (with timeout)
        _timerTask.Wait(TimeSpan.FromSeconds(5));

        // Dispose resources
        _timer.Dispose();
        _timerCts.Dispose();
        _queue.Dispose();
        _management.Clear();
    }

    /// <inheritdoc cref="IAsyncEnumerable{T}.GetAsyncEnumerator"/>
    public async IAsyncEnumerator<QueueEntry<TEntity>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        foreach (var entry in _queue.GetConsumingEnumerable(cancellationToken))
        {
            yield return entry;
        }
    }

    /// <summary>
    /// Continuously processes scheduled entries, adding ready entries to the queue.
    /// </summary>
    /// <remarks>
    /// This method runs in a background task and checks for ready entries at regular intervals.
    /// Entries that have reached their scheduled time and are not cancelled are added to the queue
    /// and removed from the management dictionary.
    /// If an unexpected error occurs, the loop logs the error and restarts automatically so that
    /// scheduled entries continue to be promoted to the ready queue. The loop only exits cleanly
    /// when <see cref="_timerCts"/> is cancelled during disposal.
    /// </remarks>
    private async Task ProcessScheduledEntriesAsync()
    {
        while (!_timerCts.IsCancellationRequested)
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(_timerCts.Token))
                {
                    var now = DateTimeOffset.UtcNow;

                    foreach (var (key, entry) in _management)
                    {
                        // Skip if cancelled
                        if (entry.IsCancelled)
                        {
                            _management.TryRemove(key, out _);
                            _logger
                                .LogTrace(
                                    """Removed cancelled scheduled entry for entity "{Identifier}".""",
                                    entry.GetEntityIdentifierString());
                            continue;
                        }

                        if (entry.EnqueueAt > now)
                        {
                            continue;
                        }

                        // Promote atomically with the intake gate: while intake is suspended nothing is
                        // promoted, and a Clear() on leadership loss cannot be raced by a concurrent
                        // promotion re-adding an entry to the ready queue after the clear.
                        lock (_gateLock)
                        {
                            if (_intakeSuspended)
                            {
                                _logger
                                    .LogTrace(
                                        """Intake suspended for {Entity}; skipping promotion of scheduled entry "{Identifier}".""",
                                        typeof(TEntity).Name,
                                        entry.GetEntityIdentifierString());
                                continue;
                            }

                            if (!_management.TryRemove(key, out _))
                            {
                                continue;
                            }

                            _queue.TryAdd(entry.ToQueueEntry());
                        }

                        _logger
                            .LogTrace(
                                """Moved scheduled entry for entity "{Identifier}" to ready queue.""",
                                entry.GetEntityIdentifierString());
                    }
                }
            }
            catch (OperationCanceledException) when (_timerCts.IsCancellationRequested)
            {
#pragma warning disable S6667
                _logger
                    .LogDebug("Timed entity queue timer loop cancelled.");
#pragma warning restore S6667
                return;
            }
            catch (Exception ex)
            {
                // An unexpected error must not kill the loop permanently, because no further
                // entries would ever be promoted to the ready queue while the operator keeps
                // running. Log the error and let the outer while-loop restart the inner loop
                // on the next tick so that scheduling resumes automatically.
                _logger
                    .LogError(
                        ex,
                        "Unexpected error in timed entity queue timer loop. Restarting loop.");
            }
        }
    }
}
