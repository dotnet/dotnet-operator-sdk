// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Optional capability of an <see cref="IEntityLabelSelector{TEntity}"/> to evaluate its selector
/// client-side against a concrete entity. Used by the shared watch mode
/// (<c>WatchStrategy.SharedPerEntity</c>), where a single watch connection per entity type receives
/// all events and dispatches them to the controllers whose selectors match.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type this selector applies to.</typeparam>
/// <remarks>
/// Implementing this interface is optional: when a label selector does not implement it, the SDK
/// parses the selector string returned by
/// <see cref="IEntityLabelSelector{TEntity}.GetLabelSelectorAsync"/> and evaluates the standard
/// Kubernetes label selector syntax against the entity's labels. Implement this interface to skip
/// that parsing or to apply matching logic that cannot be expressed as a label selector string.
/// </remarks>
public interface IClientSideEntitySelector<in TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// Determines whether the given entity matches this selector.
    /// </summary>
    /// <param name="entity">The entity received from the watch stream.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when the entity matches and should be reconciled.</returns>
    ValueTask<bool> MatchesAsync(TEntity entity, CancellationToken cancellationToken);
}
