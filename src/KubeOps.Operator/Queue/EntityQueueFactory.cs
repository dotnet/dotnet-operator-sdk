// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.Operator.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Creates <see cref="EntityQueue{TEntity}"/> delegates that resolve the required queue and logger from the DI container.
/// </summary>
internal sealed class EntityQueueFactory(IServiceProvider services)
    : IEntityQueueFactory
{
    /// <inheritdoc/>
    public EntityQueue<TEntity> Create<TEntity>()
        where TEntity : IKubernetesObject<V1ObjectMeta> =>
        (entity, type, triggerSource, timeSpan, retryCount, cancellationToken) =>
        {
            var logger = services.GetService<ILogger<EntityQueue<TEntity>>>();
            var queue = services.GetRequiredService<ITimedEntityQueue<TEntity>>();

            logger?
                .LogTrace(
                    """Queue entity "{Identifier}"{Retry} in {Seconds}s.""",
                    entity.ToIdentifierString(),
                    retryCount > 0 ? $" (Retry: {retryCount})" : string.Empty,
                    timeSpan.TotalSeconds);

            queue.Enqueue(entity, type, triggerSource, timeSpan, retryCount, cancellationToken);
        };
}
