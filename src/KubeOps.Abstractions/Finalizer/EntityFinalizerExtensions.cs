// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Abstractions.Finalizer;

/// <summary>
/// Provides extension methods for handling entity finalizers in Kubernetes resources.
/// </summary>
public static class EntityFinalizerExtensions
{
    private const byte MaxNameLength = 63;

    /// <summary>
    /// Generates a unique identifier name for the finalizer of a given Kubernetes entity.
    /// The identifier includes the group of the entity and the name of the finalizer, ensuring it conforms to Kubernetes naming conventions.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity. Must implement <see cref="IKubernetesObject{V1ObjectMeta}"/>.</typeparam>
    /// <param name="finalizer">The finalizer implementing <see cref="IEntityFinalizer{TEntity}"/> for which the identifier is generated.</param>
    /// <param name="entity">The Kubernetes entity associated with the finalizer.</param>
    /// <returns>A string representing the unique identifier for the finalizer, truncated if it exceeds the maximum allowed length for Kubernetes names.</returns>
    public static string GetIdentifierName<TEntity>(this IEntityFinalizer<TEntity> finalizer, TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var finalizerName = finalizer.GetType().Name.ToLowerInvariant();
        finalizerName = finalizerName.EndsWith("finalizer") ? finalizerName : $"{finalizerName}finalizer";

        var entityGroupName = entity.GetKubernetesEntityAttribute()?.Group ?? string.Empty;
        var name = $"{entityGroupName}/{finalizerName}".TrimStart('/');

        return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
    }
}
