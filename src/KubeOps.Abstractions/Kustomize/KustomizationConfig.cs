// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;

namespace KubeOps.Abstractions.Kustomize;

/// <summary>
/// (Partial) definition for a kustomization yaml.
/// </summary>
public class KustomizationConfig : KubernetesObject
{
    public KustomizationConfig()
    {
        ApiVersion = "kustomize.config.k8s.io/v1beta1";
        Kind = "Kustomization";
    }

    /// <summary>
    /// Namespace that should be set.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Name prefix that should be set.
    /// </summary>
    public string? NamePrefix { get; set; }

    /// <summary>
    /// Common labels for the resources.
    /// </summary>
    public KustomizationCommonLabels[]? Labels { get; set; }

    /// <summary>
    /// Resource list.
    /// </summary>
    public IList<string>? Resources { get; set; }

    /// <summary>
    /// List of merge patches.
    /// </summary>
    public IList<string>? PatchesStrategicMerge { get; set; }

    /// <summary>
    /// List of <see cref="KustomizationImage"/>.
    /// </summary>
    public IList<KustomizationImage>? Images { get; set; }

    /// <summary>
    /// List of <see cref="KustomizationConfigMapGenerator"/>.
    /// </summary>
    public IList<KustomizationConfigMapGenerator>? ConfigMapGenerator { get; set; }

    /// <summary>
    /// List of <see cref="KustomizationSecretGenerator"/>.
    /// </summary>
    public IList<KustomizationSecretGenerator>? SecretGenerator { get; set; }
}
