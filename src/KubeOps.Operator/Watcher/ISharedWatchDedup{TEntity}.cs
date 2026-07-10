// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Operator.Watcher;

/// <summary>
/// The generation/resourceVersion deduplication a watcher applies to steady-state events, exposed so the
/// <see cref="SharedPipelineDispatcher{TEntity}"/> can make the shared dedup decision exactly once per
/// event while still evaluating membership transitions (entry/exit) before it. Implemented by
/// <see cref="ResourceWatcher{TEntity}"/>.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type.</typeparam>
internal interface ISharedWatchDedup<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>Determines whether the event is a duplicate of the last recorded token.</summary>
    /// <param name="eventType">The watch event type.</param>
    /// <param name="entity">The entity received from the watch stream.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when the event should be skipped as a duplicate.</returns>
    Task<bool> IsDuplicateAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken);

    /// <summary>Records the deduplication token for a non-<c>Deleted</c> event.</summary>
    /// <param name="entity">The entity received from the watch stream.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the token has been recorded.</returns>
    Task RecordDedupTokenAsync(TEntity entity, CancellationToken cancellationToken);

    /// <summary>Removes the deduplication token(s) for a deleted entity.</summary>
    /// <param name="entity">The entity received from the watch stream.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the token(s) have been removed.</returns>
    Task RemoveDedupTokenAsync(TEntity entity, CancellationToken cancellationToken);
}
