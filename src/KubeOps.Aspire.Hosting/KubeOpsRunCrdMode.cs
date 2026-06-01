// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Aspire.Hosting;

/// <summary>
/// Defines how CRDs are handled for a local Aspire run.
/// </summary>
public enum KubeOpsRunCrdMode
{
    /// <summary>
    /// Create missing CRDs before run and remove only CRDs created by this run on shutdown.
    /// </summary>
    Ephemeral,

    /// <summary>
    /// Create or update CRDs before run and leave them in the cluster after shutdown.
    /// </summary>
    Persistent,

    /// <summary>
    /// Require CRDs to exist before run and fail otherwise.
    /// </summary>
    RequireExisting,

    /// <summary>
    /// Do not check or manage CRDs during run.
    /// </summary>
    Skip,
}
