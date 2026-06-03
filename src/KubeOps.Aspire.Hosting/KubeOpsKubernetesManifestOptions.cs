// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Aspire.Hosting;

/// <summary>
/// Options for adding KubeOps generated Kubernetes resources to Aspire's
/// Kubernetes publish output.
/// </summary>
public sealed class KubeOpsKubernetesManifestOptions
{
    /// <summary>
    /// Gets or sets the executable used to invoke the KubeOps CLI.
    /// </summary>
    public string KubeOpsCliExecutable { get; set; } = "kubeops";

    /// <summary>
    /// Gets the arguments placed before the KubeOps command.
    /// </summary>
    /// <remarks>
    /// This is useful when invoking a local tool or project, for example
    /// <c>dotnet tool run kubeops --</c> or
    /// <c>dotnet run --project ./src/KubeOps.Cli/KubeOps.Cli.csproj --</c>.
    /// </remarks>
    public IList<string> KubeOpsCliArguments { get; } = [];

    /// <summary>
    /// Gets or sets the Kubernetes namespace used in generated RBAC subjects.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Gets or sets the Kubernetes service account used by the Aspire generated workload.
    /// </summary>
    public string ServiceAccountName { get; set; } = "default";

    /// <summary>
    /// Gets a value indicating whether generated CRDs are included in the Kubernetes output.
    /// </summary>
    public bool IncludeCrds { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether generated RBAC resources are included in the Kubernetes output.
    /// </summary>
    public bool IncludeRbac { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the configured service account is included in the Kubernetes output.
    /// </summary>
    public bool IncludeServiceAccount { get; private set; } = true;

    /// <summary>
    /// Gets or sets the operator name passed to the KubeOps generator.
    /// </summary>
    public string? OperatorName { get; set; }

    /// <summary>
    /// Gets or sets the target framework passed to the KubeOps generator.
    /// </summary>
    public string? TargetFramework { get; set; }

    /// <summary>
    /// Gets or sets the docker image name passed to the KubeOps generator.
    /// </summary>
    public string DockerImage { get; set; } = "operator";

    /// <summary>
    /// Gets or sets the docker image tag passed to the KubeOps generator.
    /// </summary>
    public string DockerImageTag { get; set; } = "latest";

    /// <summary>
    /// Configures the command used to invoke the KubeOps CLI.
    /// </summary>
    /// <param name="executable">The executable to run.</param>
    /// <param name="arguments">Optional arguments to place before the KubeOps command.</param>
    /// <returns>The configured options.</returns>
    public KubeOpsKubernetesManifestOptions UseKubeOpsCli(string executable, params string[] arguments)
    {
        KubeOpsCliExecutable = executable;
        KubeOpsCliArguments.Clear();

        foreach (var argument in arguments)
        {
            KubeOpsCliArguments.Add(argument);
        }

        return this;
    }

    /// <summary>
    /// Includes generated CRDs in the Kubernetes output.
    /// </summary>
    /// <returns>The configured options.</returns>
    public KubeOpsKubernetesManifestOptions GenerateCrds()
    {
        IncludeCrds = true;
        return this;
    }

    /// <summary>
    /// Excludes generated CRDs from the Kubernetes output.
    /// </summary>
    /// <returns>The configured options.</returns>
    public KubeOpsKubernetesManifestOptions SkipCrds()
    {
        IncludeCrds = false;
        return this;
    }

    /// <summary>
    /// Includes generated RBAC resources in the Kubernetes output.
    /// </summary>
    /// <returns>The configured options.</returns>
    public KubeOpsKubernetesManifestOptions GenerateRbac()
    {
        IncludeRbac = true;
        return this;
    }

    /// <summary>
    /// Excludes generated RBAC resources from the Kubernetes output.
    /// </summary>
    /// <returns>The configured options.</returns>
    public KubeOpsKubernetesManifestOptions SkipRbac()
    {
        IncludeRbac = false;
        return this;
    }

    /// <summary>
    /// Configures the service account used by the Aspire generated workload.
    /// </summary>
    /// <param name="name">The service account name.</param>
    /// <returns>The configured options.</returns>
    public KubeOpsKubernetesManifestOptions WithServiceAccount(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ServiceAccountName = name;
        IncludeServiceAccount = true;
        return this;
    }
}
