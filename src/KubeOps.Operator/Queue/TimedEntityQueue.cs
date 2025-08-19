// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;

using k8s;
using k8s.Models;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Represents a queue that's used to inspect a Kubernetes entity again after a given time.
/// The given enumerable only contains items that should be considered for reconciliations.
/// </summary>
/// <typeparam name="TEntity">The type of the inner entity.</typeparam>
public sealed class TimedEntityQueue<TEntity> : IDisposable
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    // A shared task factory for all the created tasks.
    private readonly TaskFactory _scheduledEntries = new(TaskScheduler.Current);

    // Used for managing all the tasks that should add something to the queue.
    private readonly ConcurrentDictionary<string, TimedQueueEntry<TEntity>> _management = new();

    // The actual queue containing all the entries that have to be reconciled.
    private readonly BlockingCollection<TEntity> _queue = new(new ConcurrentQueue<TEntity>());

    internal int Count => _management.Count;

    /// <summary>
    /// Enqueues the given <paramref name="entity"/> to happen in <paramref name="requeueIn"/>.
    /// If the item already exists, the existing entry is updated.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="requeueIn">The time after <see cref="DateTimeOffset.Now"/>, where the item is reevaluated again.</param>
    public void Enqueue(TEntity entity, TimeSpan requeueIn)
    {
        _management.AddOrUpdate(
            TimedEntityQueue<TEntity>.GetKey(entity) ?? throw new InvalidOperationException("Cannot enqueue entities without name."),
            key =>
            {
                var entry = new TimedQueueEntry<TEntity>(entity, requeueIn);
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
                oldEntry.Cancel();
                var entry = new TimedQueueEntry<TEntity>(entity, requeueIn);
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

    public void Dispose()
    {
        _queue.Dispose();
        foreach (var entry in _management.Values)
        {
            entry.Dispose();
        }
    }

    public async IAsyncEnumerator<TEntity> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        foreach (var entry in _queue.GetConsumingEnumerable(cancellationToken))
        {
            yield return entry;
        }
    }

    public void Remove(TEntity entity)
    {
        var key = TimedEntityQueue<TEntity>.GetKey(entity);
        if (key is null)
        {
            return;
        }

        if (_management.Remove(key, out var task))
        {
            task.Cancel();
        }
    }

    private static string? GetKey(TEntity entity)
    {
        if (entity.Name() is null)
        {
            return null;
        }

        if (entity.Namespace() is null)
        {
            return entity.Name();
        }

        return $"{entity.Namespace()}/{entity.Name()}";
    }
}
