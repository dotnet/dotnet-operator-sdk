// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Provides extension methods for the <see cref="ITimedEntityQueue{TEntity}"/> interface.
/// </summary>
public static class TimedEntityQueueExtensions
{
    /// <summary>
    /// Retrieves a unique key for the specified Kubernetes entity. The key is constructed
    /// using the entity's namespace and name, if available. If the entity does not have
    /// a valid name, the method returns null.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The type of the Kubernetes entity. Must implement <see cref="IKubernetesObject{V1ObjectMeta}"/>.
    /// </typeparam>
    /// <param name="queue">
    /// The timed entity queue from which the key should be derived.
    /// </param>
    /// <param name="entity">
    /// The Kubernetes entity for which the key will be retrieved.
    /// </param>
    /// <returns>
    /// A string representing the unique key for the entity, or null if the entity does not have a valid name.
    /// </returns>
    // ReSharper disable once UnusedParameter.Global
    public static string? GetKey<TEntity>(this ITimedEntityQueue<TEntity> queue, TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        if (string.IsNullOrWhiteSpace(entity.Name()))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(entity.Namespace())
            ? entity.Name()
            : $"{entity.Namespace()}/{entity.Name()}";
    }
}
