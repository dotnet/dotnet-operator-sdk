// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Kustomize;

/// <summary>
/// Definition for an "image" in a kustomization yaml.
/// </summary>
public class KustomizationImage
{
    /// <summary>
    /// Name of the image.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// New name of the image.
    /// </summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>
    /// New tag of the image.
    /// </summary>
    public string NewTag { get; set; } = string.Empty;
}
