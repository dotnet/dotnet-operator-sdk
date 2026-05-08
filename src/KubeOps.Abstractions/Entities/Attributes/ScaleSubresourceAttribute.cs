// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Enables the scale subresource on a Custom Resource Definition, allowing
/// Kubernetes HorizontalPodAutoscalers (HPAs) to scale the resource.
/// </summary>
/// <param name="specReplicasPath">JSON path to desired replicas in spec, e.g. <c>.spec.replicas</c>.</param>
/// <param name="statusReplicasPath">JSON path to observed replicas in status, e.g. <c>.status.replicas</c>.</param>
/// <param name="labelSelectorPath">Optional JSON path to the label selector, e.g. <c>.status.selector</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScaleSubresourceAttribute(
    string specReplicasPath,
    string statusReplicasPath,
    string? labelSelectorPath = null) : Attribute
{
    public string SpecReplicasPath => specReplicasPath;

    public string StatusReplicasPath => statusReplicasPath;

    public string? LabelSelectorPath => labelSelectorPath;
}
