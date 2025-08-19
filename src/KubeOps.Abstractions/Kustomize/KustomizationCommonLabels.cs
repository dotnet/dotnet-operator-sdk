// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Kustomize;

/// <summary>
/// Common labels for the resources.
/// </summary>
public class KustomizationCommonLabels
{
    public KustomizationCommonLabels(IDictionary<string, string> pairs)
    {
        foreach (var keyValuePair in pairs)
        {
            Pairs.Add(keyValuePair.Key, keyValuePair.Value);
        }
    }

    /// <summary>
    /// Include selectors.
    /// </summary>
    public bool IncludeSelectors { get; init; } = true;

    /// <summary>
    /// A list of common labels.
    /// </summary>
    public KustomizationCommonLabelsPair Pairs { get; set; } = new KustomizationCommonLabelsPair();
}
