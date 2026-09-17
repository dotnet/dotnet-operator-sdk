// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Cli;

internal enum RbacScope
{
    /// <summary>
    /// Determine the scope from the operator configuration found in the source code.
    /// </summary>
    Auto,

    /// <summary>
    /// Always generate cluster wide RBAC resources.
    /// </summary>
    Cluster,

    /// <summary>
    /// Always generate namespaced RBAC resources.
    /// </summary>
    Namespaced,
}
