// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.LabelSelectors;

public static class Extensions
{
    /// <summary>
    /// Convert an enumerable list of <see cref="LabelSelector"/>s to a string.
    /// </summary>
    /// <param name="selectors">The list of selectors.</param>
    /// <returns>A comma-joined string with all selectors converted to their expressions.</returns>
    public static string ToExpression(this IEnumerable<LabelSelector> selectors) =>
        string.Join(",", selectors.Select(x => (string)x));
}
