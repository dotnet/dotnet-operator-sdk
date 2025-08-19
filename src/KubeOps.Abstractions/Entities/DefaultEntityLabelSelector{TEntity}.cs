// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Entities;

public class DefaultEntityLabelSelector<TEntity> : IEntityLabelSelector<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
}
