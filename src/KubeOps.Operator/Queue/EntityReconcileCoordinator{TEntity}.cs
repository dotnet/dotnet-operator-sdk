// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Default <see cref="IEntityReconcileCoordinator{TEntity}"/>. Registered as a singleton per entity type
/// so every controller pipeline (queue consumer) of that type shares one parallelism budget and one
/// per-UID lock registry. Disposed only when the DI container is torn down, so it outlives the
/// leadership-loss restart of an individual consumer.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type this coordinator serializes.</typeparam>
internal sealed class EntityReconcileCoordinator<TEntity>
    : IEntityReconcileCoordinator<TEntity>, IDisposable, IAsyncDisposable
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly ConcurrentDictionary<string, UidEntry> _uidEntries = new();
    private readonly object _entriesLock = new();
    private readonly SemaphoreSlim _parallelismSemaphore;

    public EntityReconcileCoordinator(OperatorSettings settings)
        => _parallelismSemaphore = new(
            settings.ParallelReconciliation.MaxParallelReconciliations,
            settings.ParallelReconciliation.MaxParallelReconciliations);

    public Task AcquireParallelSlotAsync(CancellationToken cancellationToken) =>
        _parallelismSemaphore.WaitAsync(cancellationToken);

    public void ReleaseParallelSlot() => _parallelismSemaphore.Release();

    public async Task<IAsyncDisposable?> AcquireEntityLockAsync(
        TEntity entity,
        ParallelReconciliationConflictStrategy strategy,
        CancellationToken cancellationToken)
    {
        var uid = entity.Uid();
        UidEntry entry;
        lock (_entriesLock)
        {
            entry = _uidEntries.GetOrAdd(uid, _ => new(new(1, 1)));
            entry.AccessCount++;
        }

        try
        {
            var acquired = strategy switch
            {
                ParallelReconciliationConflictStrategy.Discard or ParallelReconciliationConflictStrategy.RequeueAfterDelay =>
                    await entry.Semaphore.WaitAsync(0, cancellationToken),
                ParallelReconciliationConflictStrategy.WaitForCompletion =>
                    await WaitForCompletionAsync(entry.Semaphore, cancellationToken),
                _ => throw new NotSupportedException($"Conflict strategy {strategy} is not supported."),
            };

            if (!acquired)
            {
                // Contended and the strategy is non-blocking: no lock held, so only release the entry reference.
                ReleaseEntry(uid, entry);
                return null;
            }

            return new EntityLock(this, uid, entry);
        }
        catch
        {
            // The wait was cancelled (or faulted) before the semaphore was acquired. Roll back the entry
            // reference so an unused UidEntry is removed, without releasing a semaphore that was never taken.
            ReleaseEntry(uid, entry);
            throw;
        }
    }

    public void Dispose()
    {
        _parallelismSemaphore.Dispose();

        lock (_entriesLock)
        {
            foreach (var entry in _uidEntries.Values)
            {
                entry.Semaphore.Dispose();
            }

            _uidEntries.Clear();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<bool> WaitForCompletionAsync(SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return true;
    }

    private void ReleaseEntry(string uid, UidEntry entry)
    {
        lock (_entriesLock)
        {
            if (--entry.AccessCount == 0)
            {
                _uidEntries.TryRemove(uid, out _);
            }
        }
    }

    private sealed record UidEntry(SemaphoreSlim Semaphore)
    {
        public int AccessCount { get; set; }
    }

    private sealed class EntityLock(EntityReconcileCoordinator<TEntity> owner, string uid, UidEntry entry)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            entry.Semaphore.Release();
            owner.ReleaseEntry(uid, entry);
            return ValueTask.CompletedTask;
        }
    }
}
