// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Entities;

#pragma warning disable S2326
public interface IEntityFieldSelector<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    ValueTask<string?> GetFieldSelectorAsync(CancellationToken cancellationToken);
}
#pragma warning restore S2326
