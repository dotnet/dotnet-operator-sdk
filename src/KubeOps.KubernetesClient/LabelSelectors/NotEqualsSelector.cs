// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.LabelSelectors;

/// <summary>
/// Label-selector that checks if a certain label does not contain
/// a specific value (out of a list of values).
/// Note that "label notin (value)" is the same as "label != value".
/// </summary>
/// <param name="Label">The label that must not equal to one of the values.</param>
/// <param name="Values">The possible values.</param>
public record NotEqualsSelector(string Label, params string[] Values) : LabelSelector
{
    protected override string ToExpression() => $"{Label} notin ({string.Join(",", Values)})";
}
