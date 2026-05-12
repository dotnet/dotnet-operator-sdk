// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Default implementation of <see cref="IEntityLabelSelector{TEntity}"/> that applies no label selector,
/// causing the watcher to observe all entities of type <typeparamref name="TEntity"/>.
/// Replace this registration with a custom implementation to narrow the watch to entities matching specific labels.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type this selector applies to.</typeparam>
public sealed class DefaultEntityLabelSelector<TEntity> : IEntityLabelSelector<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <inheritdoc />
    public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>(null);
}
