// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.LeaderElection;

/// <summary>
/// An <see cref="ILeadershipScope"/> that partitions responsibility by Kubernetes namespace.
/// Derive from this class and implement <see cref="IsResponsibleForNamespaceAsync"/>.
/// </summary>
public abstract class NamespacedLeadershipScope : ILeadershipScope
{
    /// <inheritdoc/>
    public event Action? ScopeChanged;

    /// <inheritdoc/>
    public ValueTask<bool> IsResponsibleForAsync(
        IKubernetesObject<V1ObjectMeta> entity,
        CancellationToken cancellationToken)
        => IsResponsibleForNamespaceAsync(entity.Namespace(), cancellationToken);

    /// <summary>
    /// Determines whether this instance is responsible for entities in the given namespace.
    /// </summary>
    /// <param name="namespace">
    /// The namespace of the entity, or <see langword="null"/> for cluster-scoped entities.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when this instance must process the namespace.</returns>
    protected abstract ValueTask<bool> IsResponsibleForNamespaceAsync(
        string? @namespace,
        CancellationToken cancellationToken);

    /// <summary>
    /// Raises <see cref="ScopeChanged"/>. Call when the set of responsible namespaces changes.
    /// </summary>
    protected void OnScopeChanged() => ScopeChanged?.Invoke();
}
