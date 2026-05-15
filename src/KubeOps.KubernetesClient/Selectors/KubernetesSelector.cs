// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.Selectors;

/// <summary>
/// Common base record for all Kubernetes selector types (label and field selectors).
/// </summary>
public abstract record KubernetesSelector
{
    /// <summary>
    /// Cast the selector to a string expression.
    /// </summary>
    /// <param name="selector">The selector.</param>
    /// <returns>A string representation of the selector.</returns>
    public static implicit operator string(KubernetesSelector selector) => selector.ToExpression();

    /// <summary>
    /// Create an expression string from the selector.
    /// </summary>
    /// <returns>A string that represents the selector expression.</returns>
    protected abstract string ToExpression();
}
