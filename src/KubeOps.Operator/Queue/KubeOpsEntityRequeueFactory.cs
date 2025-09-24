// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation.Queue;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

internal sealed class KubeOpsEntityRequeueFactory(IServiceProvider services)
    : IEntityRequeueFactory
{
    public EntityRequeue<TEntity> Create<TEntity>()
        where TEntity : IKubernetesObject<V1ObjectMeta> =>
        (entity, type, timeSpan) =>
        {
            var logger = services.GetService<ILogger<EntityRequeue<TEntity>>>();
            var queue = services.GetRequiredService<ITimedEntityQueue<TEntity>>();

            logger?.LogTrace(
                """Requeue entity "{Kind}/{Name}" in {Milliseconds}ms.""",
                entity.Kind,
                entity.Name(),
                timeSpan.TotalMilliseconds);

            queue.Enqueue(entity, type, timeSpan);
        };
}
