// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation.Controller;

/// <summary>
/// Generic entity controller. The controller manages the reconcile loop
/// for a given entity type.
/// </summary>
/// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
/// <example>
/// Simple example controller that just logs the entity.
/// <code>
/// public class V1TestEntityController : IEntityController&lt;V1TestEntity&gt;
/// {
///     private readonly ILogger&lt;V1TestEntityController&gt; _logger;
///
///     public V1TestEntityController(
///         ILogger&lt;V1TestEntityController&gt; logger)
///     {
///         _logger = logger;
///     }
///
///     public async Task ReconcileAsync(V1TestEntity entity, CancellationToken token)
///     {
///         _logger.LogInformation("Reconciling entity {Entity}.", entity);
///     }
///
///     public async Task DeletedAsync(V1TestEntity entity, CancellationToken token)
///     {
///         _logger.LogInformation("Deleting entity {Entity}.", entity);
///     }
/// }
/// </code>
/// </example>
public interface IEntityController<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// Returns <c>true</c> when this controller is responsible for the given entity.
    /// The default implementation returns <c>true</c>, preserving single-controller backward-compatible behaviour.
    ///
    /// When multiple controllers are registered for the same entity type the reconciler asks every
    /// controller whether it <see cref="ShouldHandle"/> the entity and dispatches to all that claim
    /// responsibility in registration order. Typical use cases: filtering by labels, annotations,
    /// namespace, status conditions, or any other entity-derived predicate the consumer needs.
    /// </summary>
    /// <param name="entity">The entity the reconciler is about to dispatch.</param>
    /// <param name="cancellationToken">The token used to signal cancellation of the operation.</param>
    /// <returns>A <see cref="ValueTask{Boolean}"/> that resolves to <c>true</c> if this controller should reconcile the entity.</returns>
    ValueTask<bool> ShouldHandle(TEntity entity, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

    /// <summary>
    /// Reconciles the state of the specified entity with the desired state.
    /// This method is triggered for `added` and `modified` events from the watcher.
    /// </summary>
    /// <param name="entity">The entity that initiated the reconcile operation.</param>
    /// <param name="cancellationToken">The token used to signal cancellation of the operation.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result of the reconcile process.</returns>
    Task<ReconciliationResult<TEntity>> ReconcileAsync(TEntity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Called for `delete` events for a given entity.
    /// </summary>
    /// <param name="entity">The entity that fired the deleted event.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result of the reconcile process.</returns>
    Task<ReconciliationResult<TEntity>> DeletedAsync(TEntity entity, CancellationToken cancellationToken);
}
