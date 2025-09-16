// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Queue;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Represents a queue that's used to inspect a Kubernetes entity again after a given time.
/// The given enumerable only contains items that should be considered for reconciliations.
/// </summary>
/// <typeparam name="TEntity">The type of the inner entity.</typeparam>
public sealed class TimedEntityQueue<TEntity>(
    ILogger<TimedEntityQueue<TEntity>> logger)
    : ITimedEntityQueue<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    // A shared task factory for all the created tasks.
    private readonly TaskFactory _scheduledEntries = new(TaskScheduler.Current);

    // Used for managing all the tasks that should add something to the queue.
    private readonly ConcurrentDictionary<string, TimedQueueEntry<TEntity>> _management = new();

    // The actual queue containing all the entries that have to be reconciled.
    private readonly BlockingCollection<RequeueEntry<TEntity>> _queue = new(new ConcurrentQueue<RequeueEntry<TEntity>>());

    internal int Count => _management.Count;

    /// <inheritdoc cref="ITimedEntityQueue{TEntity}.Enqueue"/>
    public void Enqueue(TEntity entity, RequeueType type, TimeSpan requeueIn)
    {
        _management
            .AddOrUpdate(
                this.GetKey(entity) ?? throw new InvalidOperationException("Cannot enqueue entities without name."),
                key =>
                {
                    logger.LogTrace(
                        """Adding schedule for entity "{Kind}/{Name}" to reconcile in {Seconds}s.""",
                        entity.Kind,
                        entity.Name(),
                        requeueIn.TotalSeconds);

                    var entry = new TimedQueueEntry<TEntity>(entity, type, requeueIn);
                    _scheduledEntries.StartNew(
                        async () =>
                        {
                            await entry.AddAfterDelay(_queue);
                            _management.TryRemove(key, out _);
                        },
                        entry.Token);
                    return entry;
                },
                (key, oldEntry) =>
                {
                    logger.LogTrace(
                        """Updating schedule for entity "{Kind}/{Name}" to reconcile in {Seconds}s.""",
                        entity.Kind,
                        entity.Name(),
                        requeueIn.TotalSeconds);

                    oldEntry.Cancel();
                    var entry = new TimedQueueEntry<TEntity>(entity, type, requeueIn);
                    _scheduledEntries.StartNew(
                        async () =>
                        {
                            await entry.AddAfterDelay(_queue);
                            _management.TryRemove(key, out _);
                        },
                        entry.Token);
                    return entry;
                });
    }

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose()
    {
        _queue.Dispose();
        foreach (var entry in _management.Values)
        {
            entry.Dispose();
        }
    }

    /// <inheritdoc cref="IAsyncEnumerable{T}.GetAsyncEnumerator"/>
    public async IAsyncEnumerator<RequeueEntry<TEntity>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        foreach (var entry in _queue.GetConsumingEnumerable(cancellationToken))
        {
            yield return entry;
        }
    }

    /// <inheritdoc cref="ITimedEntityQueue{TEntity}.Remove"/>
    public void Remove(TEntity entity)
    {
        var key = this.GetKey(entity);
        if (key is null)
        {
            return;
        }

        if (_management.Remove(key, out var task))
        {
            task.Cancel();
        }
    }
}
