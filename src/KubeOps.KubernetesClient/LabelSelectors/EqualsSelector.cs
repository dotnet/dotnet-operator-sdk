// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.LabelSelectors;

/// <summary>
/// Label-selector that checks if a certain label contains
/// a specific value (out of a list of values).
/// Note that "label in (value)" is the same as "label == value".
/// </summary>
/// <param name="Label">The label that needs to equal to one of the values.</param>
/// <param name="Values">The possible values.</param>
public record EqualsSelector(string Label, params string[] Values) : LabelSelector
{
    protected override string ToExpression() => $"{Label} in ({string.Join(",", Values)})";
}
