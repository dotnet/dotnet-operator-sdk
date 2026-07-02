// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Kubernetes;

namespace Aspire.Hosting;

/// <summary>
/// Options for running a KubeOps operator as a local Aspire project process with a Kubernetes target.
/// </summary>
public sealed class KubeOpsRunOptions
{
    internal KubeOpsRunOptions(KubernetesEnvironmentResource target)
        : this(target, _ => new ValueTask<string?>(target.KubeConfigPath))
    {
    }

    internal KubeOpsRunOptions(
        IResource targetResource,
        Func<CancellationToken, ValueTask<string?>> resolveKubeConfigPathAsync)
    {
        TargetResource = targetResource;
        Target = targetResource as KubernetesEnvironmentResource;
        ResolveKubeConfigPathAsync = resolveKubeConfigPathAsync;
    }

    /// <summary>
    /// Gets the Kubernetes environment used by the local operator process, or <see langword="null"/> when the target
    /// is a connection-string resource (e.g. a k3s cluster) rather than a <see cref="KubernetesEnvironmentResource"/>.
    /// </summary>
    public KubernetesEnvironmentResource? Target { get; }

    /// <summary>
    /// Gets the underlying target resource used by the local operator process.
    /// </summary>
    public IResource TargetResource { get; }

    /// <summary>
    /// Gets the CRD lifecycle mode used for local run.
    /// </summary>
    public KubeOpsRunCrdMode CrdMode { get; private set; } = KubeOpsRunCrdMode.Ephemeral;

    /// <summary>
    /// Resolves the kubeconfig path used to communicate with the target cluster.
    /// </summary>
    internal Func<CancellationToken, ValueTask<string?>> ResolveKubeConfigPathAsync { get; }

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
