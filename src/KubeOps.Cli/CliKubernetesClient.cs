// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;

using KubeOps.KubernetesClient;

namespace KubeOps.Cli;

internal static class CliKubernetesClient
{
    /// <summary>
    /// The field manager under which the CLI's writes (e.g. installed CRDs) are recorded in
    /// <c>metadata.managedFields</c>.
    /// </summary>
    public const string FieldManager = "kubeops-cli";

    /// <summary>
    /// Creates a client that writes with <see cref="FieldManager"/>.
    /// </summary>
    /// <param name="config">
    /// The client configuration. Defaults to the cluster of the currently selected context.
    /// </param>
    /// <returns>The client. The caller owns it.</returns>
    public static IKubernetes Create(KubernetesClientConfiguration? config = null) =>
        new Kubernetes(
            config ?? KubernetesClientConfiguration.BuildDefaultConfig(),
            new FieldManagerHandler(FieldManager));
}
