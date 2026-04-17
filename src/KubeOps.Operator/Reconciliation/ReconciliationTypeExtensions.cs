// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Operator.Reconciliation;

/// <summary>
/// Provides extension methods for converting between <see cref="WatchEventType"/> and <see cref="ReconciliationType"/>.
/// </summary>
/// <remarks>
/// These conversions allow mapping Kubernetes watch events (from the API server) to
/// reconciliation operation types that determine how the reconciler processes entities.
/// </remarks>
public static class ReconciliationTypeExtensions
{
    /// <summary>
    /// Converts a <see cref="WatchEventType"/> to its corresponding <see cref="ReconciliationType"/>.
    /// </summary>
    /// <param name="watchEventType">The Kubernetes watch event type to be converted.</param>
    /// <returns>The corresponding reconciliation operation type for the given watch event.</returns>
    /// <exception cref="NotSupportedException">Thrown when the provided <see cref="WatchEventType"/> is not supported.</exception>
    public static ReconciliationType ToReconciliationType(this WatchEventType watchEventType)
        => watchEventType switch
        {
            WatchEventType.Added => ReconciliationType.Added,
            WatchEventType.Modified => ReconciliationType.Modified,
            WatchEventType.Deleted => ReconciliationType.Deleted,
            _ => throw new NotSupportedException($"WatchEventType '{watchEventType}' is not supported!"),
        };
}
