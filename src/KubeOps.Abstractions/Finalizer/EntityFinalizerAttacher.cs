// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Finalizer;

/// <summary>
/// <para>
/// Injectable delegate for finalizers. This delegate is used to attach a finalizer
/// with its identifier to an entity. When injected, simply call the delegate with
/// the entity to attach the finalizer.
/// </para>
/// <para>
/// As with other (possibly) mutating calls, use the returned entity for further
/// modification and Kubernetes client interactions, since the resource version
/// is updated each time the entity is modified.
/// </para>
/// <para>
/// Note that the operator needs RBAC access to modify the list of
/// finalizers on the entity.
/// </para>
/// </summary>
/// <typeparam name="TImplementation">The type of the entity finalizer.</typeparam>
/// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
/// <param name="entity">The instance of the entity, that the finalizer is attached if needed.</param>
/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
/// <returns>A <see cref="Task"/> that resolves when the finalizer was attached.</returns>
/// <example>
/// Use the finalizer delegate to attach the "FinalizerOne" to the entity as soon
/// as the entity gets reconciled.
/// <code>
/// [EntityRbac(typeof(V1TestEntity), Verbs = RbacVerb.All)]
/// public class V1TestEntityController : IEntityController&lt;V1TestEntity&gt;
/// {
///     private readonly EntityFinalizerAttacher&lt;FinalizerOne, V1TestEntity&gt; _finalizer1;
///
///     public V1TestEntityController(
///         EntityFinalizerAttacher&lt;FinalizerOne, V1TestEntity&gt; finalizer1) => _finalizer1 = finalizer1;
///
///     public async Task ReconcileAsync(V1TestEntity entity, CancellationToken token)
///     {
///         entity = await _finalizer1(entity, token);
///     }
/// }
/// </code>
/// </example>
public delegate Task<TEntity> EntityFinalizerAttacher<TImplementation, TEntity>(
    TEntity entity,
    CancellationToken cancellationToken = default)
    where TImplementation : IEntityFinalizer<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>;
