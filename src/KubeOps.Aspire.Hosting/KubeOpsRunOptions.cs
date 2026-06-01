// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Aspire.Hosting.Kubernetes;

namespace Aspire.Hosting;

/// <summary>
/// Options for running a KubeOps operator as a local Aspire project process with a Kubernetes target.
/// </summary>
public sealed class KubeOpsRunOptions
{
    internal KubeOpsRunOptions(KubernetesEnvironmentResource target)
    {
        Target = target;
    }

    /// <summary>
    /// Gets the Kubernetes environment used by the local operator process.
    /// </summary>
    public KubernetesEnvironmentResource Target { get; }

    /// <summary>
    /// Gets the CRD lifecycle mode used for local run.
    /// </summary>
    public KubeOpsRunCrdMode CrdMode { get; private set; } = KubeOpsRunCrdMode.Ephemeral;

    /// <summary>
    /// Creates missing CRDs before run and removes only CRDs created by this run on shutdown.
    /// </summary>
    /// <returns>The configured options.</returns>
    public KubeOpsRunOptions WithEphemeralCrds()
    {
        CrdMode = KubeOpsRunCrdMode.Ephemeral;
        return this;
    }

    /// <summary>
    /// Creates or updates CRDs before run and leaves them in the cluster after shutdown.
    /// </summary>
    /// <returns>The configured options.</returns>
    public KubeOpsRunOptions WithPersistentCrds()
    {
        CrdMode = KubeOpsRunCrdMode.Persistent;
        return this;
    }

    /// <summary>
    /// Requires CRDs to exist before run and fails otherwise.
    /// </summary>
    /// <returns>The configured options.</returns>
    public KubeOpsRunOptions RequireExistingCrds()
    {
        CrdMode = KubeOpsRunCrdMode.RequireExisting;
        return this;
    }

    /// <summary>
    /// Skips CRD checks and management during run.
    /// </summary>
    /// <returns>The configured options.</returns>
    public KubeOpsRunOptions SkipCrds()
    {
        CrdMode = KubeOpsRunCrdMode.Skip;
        return this;
    }
}
