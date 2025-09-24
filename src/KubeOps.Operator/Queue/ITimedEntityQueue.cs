// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation.Queue;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Represents a timed queue for managing Kubernetes entities of type <typeparamref name="TEntity"/>.
/// This interface provides mechanisms to enqueue entities for later processing and remove entities from the queue.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes entity. Must implement <see cref="IKubernetesObject{V1ObjectMeta}"/>.
/// </typeparam>
public interface ITimedEntityQueue<TEntity> : IDisposable, IAsyncEnumerable<RequeueEntry<TEntity>>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// Adds the specified entity to the queue for processing after the specified time span has expired.
    /// </summary>
    /// <param name="entity">The entity to be added to the queue.</param>
    /// <param name="type">The type of requeue operation to be performed.</param>
    /// <param name="requeueIn">The duration after which the entity should be requeued.</param>
    void Enqueue(TEntity entity, RequeueType type, TimeSpan requeueIn);

    /// <summary>
    /// Removes the specified <paramref name="entity"/> from the queue.
    /// </summary>
    /// <param name="entity">The entity to be removed from the queue.</param>
    void Remove(TEntity entity);
}
