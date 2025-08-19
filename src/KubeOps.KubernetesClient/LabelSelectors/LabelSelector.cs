// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.LabelSelectors;

/// <summary>
/// Different label selectors for querying the Kubernetes API.
/// </summary>
public abstract record LabelSelector
{
    /// <summary>
    /// Cast the label selector to a string.
    /// </summary>
    /// <param name="selector">The selector.</param>
    /// <returns>A string representation of the label selector.</returns>
    public static implicit operator string(LabelSelector selector) => selector.ToExpression();

    /// <summary>
    /// Create an expression from the label selector.
    /// </summary>
    /// <returns>A string that represents the label selector.</returns>
    protected abstract string ToExpression();
}
