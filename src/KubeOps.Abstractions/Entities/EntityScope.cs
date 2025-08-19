// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Scope of the resource. Custom entities (resources) in Kubernetes
/// can either be namespaced or cluster-wide.
/// </summary>
public enum EntityScope
{
    /// <summary>
    /// The resource is namespace.
    /// </summary>
    Namespaced,

    /// <summary>
    /// The resource is cluster-wide.
    /// </summary>
    Cluster,
}
