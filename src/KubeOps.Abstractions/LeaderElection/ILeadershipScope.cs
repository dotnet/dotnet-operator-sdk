// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.LeaderElection;

/// <summary>
/// Decides which namespaces this operator instance is responsible for when
/// <see cref="Builder.LeaderElectionType.Scoped"/> leader election is active. The coordination
/// mechanism is owned entirely by the implementation.
/// </summary>
/// <remarks>
/// <see cref="IsResponsibleForAsync"/> is called frequently; implementations must answer cheaply
/// (e.g. from cached state).
/// </remarks>
public interface ILeadershipScope
{
    /// <summary>
    /// Raised when the set of namespaces this instance is responsible for changes.
    /// </summary>
    event Action<LeadershipScopeChange>? ScopeChanged;

    /// <summary>
    /// Determines whether this instance is responsible for entities in the given namespace.
    /// </summary>
    /// <param name="namespace">
    /// The namespace of the entity, or <see langword="null"/> for cluster-scoped entities.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when this instance must process the entity.</returns>
    ValueTask<bool> IsResponsibleForAsync(string? @namespace, CancellationToken cancellationToken);
}
