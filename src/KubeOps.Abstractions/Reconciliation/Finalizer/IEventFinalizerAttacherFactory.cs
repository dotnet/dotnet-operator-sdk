// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation.Finalizer;

/// <summary>
/// Represents a type used to create <see cref="EntityFinalizerAttacher{TImplementation,TEntity}"/> for controllers.
/// </summary>
public interface IEventFinalizerAttacherFactory
{
    /// <summary>
    /// Creates a new <see cref="EntityFinalizerAttacher{TImplementation,TEntity}"/>, which attaches the finalizer of
    /// type <typeparamref name="TImplementation"/> to <typeparamref name="TEntity"/>.
    /// </summary>
    /// <param name="identifier">The finalizer identifier.</param>
    /// <typeparam name="TImplementation">The finalizer.</typeparam>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <returns>A delegate to attach the finalizer implementation to the entity.</returns>
    EntityFinalizerAttacher<TImplementation, TEntity> Create<TImplementation, TEntity>(string identifier)
        where TImplementation : class, IEntityFinalizer<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>;
}
