// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Operator.Logging;

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
public sealed class TimedEntityQueue<TEntity> : ITimedEntityQueue<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// The interval at which the timer checks for entries ready to be added to the queue.
    /// </summary>
    private const int TimerIntervalMilliseconds = 100;

    private readonly ILogger<TimedEntityQueue<TEntity>> _logger;

    // Used for managing all scheduled entries that should be added to the queue in the future.
    private readonly ConcurrentDictionary<string, TimedQueueEntry<TEntity>> _management = new();

    // The actual queue containing all the entries that have to be reconciled.
    private readonly BlockingCollection<QueueEntry<TEntity>> _queue = new(new ConcurrentQueue<QueueEntry<TEntity>>());

    // Periodic timer that checks for ready entries.
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(TimerIntervalMilliseconds));

    // Cancellation token source for the timer loop.
    private readonly CancellationTokenSource _timerCts = new();

    // Task that runs the timer loop.
    private readonly Task _timerTask;

    private bool _disposed;

    public TimedEntityQueue(ILogger<TimedEntityQueue<TEntity>> logger)
    {
        _logger = logger;
        _timerTask = Task.Run(ProcessScheduledEntriesAsync);
    }

    internal int Count => _management.Count;

    /// <inheritdoc cref="ITimedEntityQueue{TEntity}.Enqueue"/>
    public Task Enqueue(TEntity entity, ReconciliationType type, ReconciliationTriggerSource reconciliationTriggerSource, TimeSpan queueIn, CancellationToken cancellationToken)
    {
        var key = this.GetKey(entity) ?? throw new InvalidOperationException("Cannot enqueue entities without name.");

        _management
            .AddOrUpdate(
                key,
                _ =>
                {
                    _logger
                        .LogTrace(
                            """Adding schedule for entity "{Identifier}" to reconcile in {Seconds}s.""",
                            entity.ToIdentifierString(),
                            queueIn.TotalSeconds);

                    return new(entity, type, reconciliationTriggerSource, queueIn);
                },
                (_, oldEntry) =>
                {
                    // the entry with the earliest execution time should be kept,
                    // so only update if the new entry is scheduled to be executed sooner
                    // than the existing one
                    if (oldEntry.EnqueueAt <= DateTimeOffset.UtcNow.Add(queueIn))
                    {
                        _logger
                            .LogTrace(
                                """Keeping existing schedule for entity "{Identifier}" to reconcile in {Seconds}s.""",
                                entity.ToIdentifierString(),
                                oldEntry.EnqueueAt.Subtract(DateTimeOffset.UtcNow).TotalSeconds);

                        return oldEntry;
                    }

                    _logger
                        .LogTrace(
                            """Updating schedule for entity "{Identifier}" to reconcile in {Seconds}s.""",
                            entity.ToIdentifierString(),
                            queueIn.TotalSeconds);

                    oldEntry.Cancel();
                    return new(entity, type, reconciliationTriggerSource, queueIn);
                });

        return Task.CompletedTask;
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

    /// <inheritdoc cref="ITimedEntityQueue{TEntity}.Remove"/>
    public Task Remove(TEntity entity, CancellationToken cancellationToken)
    {
        var key = this.GetKey(entity);
        if (key is null)
        {
            return Task.CompletedTask;
        }

        if (_management.Remove(key, out var entry))
        {
            entry.Cancel();
        }

        return Task.CompletedTask;
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

                    // Check all scheduled entries
                    foreach (var (key, entry) in _management)
                    {
                        // Skip if cancelled
                        if (entry.IsCancelled)
                        {
                            _management.TryRemove(key, out _);
                            continue;
                        }

                        // Check if entry is ready to be added to the queue and remove from management
                        if (entry.EnqueueAt <= now && _management.TryRemove(key, out _))
                        {
                            var queueEntry = entry.ToQueueEntry();
                            _queue.TryAdd(queueEntry);

                            _logger
                                .LogTrace(
                                    """Entity "{Identifier}" is queued for reconciliation.""",
                                    queueEntry.Entity.ToIdentifierString());
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_timerCts.IsCancellationRequested)
            {
                // Expected during disposal – exit the outer loop cleanly.
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
