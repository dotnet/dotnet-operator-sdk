// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Cli.Transpilation;

internal enum OperatorWatchScopeKind
{
    /// <summary>
    /// The operator watches resources across the cluster.
    /// </summary>
    ClusterWide,

    /// <summary>
    /// The operator watches resources in one namespace.
    /// </summary>
    Namespaced,

    /// <summary>
    /// The operator watch scope cannot be determined statically.
    /// </summary>
    Unknown,
}
