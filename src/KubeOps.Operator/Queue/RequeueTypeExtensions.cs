// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;

using KubeOps.Abstractions.Reconciliation.Queue;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Provides extension methods for converting between <see cref="WatchEventType"/> and <see cref="RequeueType"/>.
/// </summary>
public static class RequeueTypeExtensions
{
    /// <summary>
    /// Converts a <see cref="WatchEventType"/> to its corresponding <see cref="RequeueType"/>.
    /// </summary>
    /// <param name="watchEventType">The watch event type to be converted.</param>
    /// <returns>The corresponding <see cref="RequeueType"/> for the given <see cref="WatchEventType"/>.</returns>
    /// <exception cref="NotSupportedException">Thrown when the provided <see cref="WatchEventType"/> is not supported.</exception>
    public static RequeueType ToRequeueType(this WatchEventType watchEventType)
        => watchEventType switch
        {
            WatchEventType.Added => RequeueType.Added,
            WatchEventType.Modified => RequeueType.Modified,
            WatchEventType.Deleted => RequeueType.Deleted,
            _ => throw new NotSupportedException($"WatchEventType '{watchEventType}' is not supported!"),
        };

    /// <summary>
    /// Converts a <see cref="RequeueType"/> to its corresponding <see cref="WatchEventType"/>.
    /// </summary>
    /// <param name="requeueType">The requeue type to be converted.</param>
    /// <returns>The corresponding <see cref="WatchEventType"/> for the given <see cref="RequeueType"/>.</returns>
    /// <exception cref="NotSupportedException">Thrown when the provided <see cref="RequeueType"/> is not supported.</exception>
    public static WatchEventType ToWatchEventType(this RequeueType requeueType)
        => requeueType switch
        {
            RequeueType.Added => WatchEventType.Added,
            RequeueType.Modified => WatchEventType.Modified,
            RequeueType.Deleted => WatchEventType.Deleted,
            _ => throw new NotSupportedException($"RequeueType '{requeueType}' is not supported!"),
        };
}
