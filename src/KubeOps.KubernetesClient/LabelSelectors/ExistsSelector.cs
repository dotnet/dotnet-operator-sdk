// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.LabelSelectors;

/// <summary>
/// Selector that checks if a certain label exists.
/// </summary>
/// <param name="Label">The label that needs to exist on the entity/resource.</param>
public record ExistsSelector(string Label) : LabelSelector
{
    protected override string ToExpression() => $"{Label}";
}
