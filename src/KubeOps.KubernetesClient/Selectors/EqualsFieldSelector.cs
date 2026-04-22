// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.Selectors;

/// <summary>
/// Field-selector that checks if a certain field equals a specific value.
/// Produces the expression <c>field=value</c>.
/// </summary>
/// <param name="Field">The field path (e.g. <c>metadata.name</c>).</param>
/// <param name="Value">The required value.</param>
public record EqualsFieldSelector(string Field, string Value) : FieldSelector
{
    protected override string ToExpression() => $"{Field}={Value}";
}
