// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.LeaderElection;

/// <summary>
/// Describes a change of the namespaces an operator instance is responsible for under
/// <see cref="Builder.LeaderElectionType.Scoped"/> leader election.
/// </summary>
/// <param name="AcquiredNamespaces">Namespaces this instance became responsible for.</param>
/// <param name="LostNamespaces">Namespaces this instance is no longer responsible for.</param>
public sealed record LeadershipScopeChange(
    IReadOnlyCollection<string> AcquiredNamespaces,
    IReadOnlyCollection<string> LostNamespaces);
