// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.LeaderElection;

/// <summary>
/// Decides which entities this operator instance is responsible for when
/// <see cref="Builder.LeaderElectionType.Scoped"/> leader election is active. The partitioning
/// dimension (namespace, labels, name hashing, ...) and the coordination mechanism are owned
/// entirely by the implementation. For the common namespace partitioning, derive from
/// <see cref="NamespacedLeadershipScope"/>.
/// </summary>
/// <remarks>
/// <see cref="IsResponsibleForAsync"/> is called frequently; implementations must answer cheaply
/// (e.g. from cached state).
/// </remarks>
public interface ILeadershipScope
{
    /// <summary>
    /// Raised when the set of entities this instance is responsible for changes.
    /// </summary>
    event Action? ScopeChanged;

    /// <summary>
    /// Determines whether this instance is responsible for the given entity.
    /// </summary>
    /// <param name="entity">The entity to decide responsibility for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when this instance must process the entity.</returns>
    ValueTask<bool> IsResponsibleForAsync(
        IKubernetesObject<V1ObjectMeta> entity,
        CancellationToken cancellationToken);
}
