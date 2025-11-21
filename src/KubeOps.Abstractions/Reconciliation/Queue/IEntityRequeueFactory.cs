// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation.Queue;

/// <summary>
/// Represents a type used to create delegates of type <see cref="EntityRequeue{TEntity}"/> for requeuing entities.
/// </summary>
public interface IEntityRequeueFactory
{
    /// <summary>
    /// Creates a new <see cref="EntityRequeue{TEntity}"/> for the given <typeparamref name="TEntity"/> type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>A <see cref="EntityRequeue{TEntity}"/>.</returns>
    EntityRequeue<TEntity> Create<TEntity>()
        where TEntity : IKubernetesObject<V1ObjectMeta>;
}
