// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.Selectors;

/// <summary>
/// Selector that checks if a certain label does not exist.
/// </summary>
/// <param name="Label">The label that must not exist on the entity/resource.</param>
public record NotExistsLabelSelector(string Label) : LabelSelector
{
    protected override string ToExpression() => $"!{Label}";
}
