// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Finalizer;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Finalizer;

internal sealed class KubeOpsEventFinalizerAttacherFactory(ILoggerFactory loggerFactory, IKubernetesClient client)
    : IEventFinalizerAttacherFactory
{
    public EntityFinalizerAttacher<TImplementation, TEntity> Create<TImplementation, TEntity>(string identifier)
        where TImplementation : class, IEntityFinalizer<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var logger = loggerFactory.CreateLogger<EntityFinalizerAttacher<TImplementation, TEntity>>();
        return (entity, token) =>
        {
            logger.LogTrace(
                """Try to add finalizer "{Finalizer}" on entity "{Kind}/{Name}".""",
                identifier,
                entity.Kind,
                entity.Name());

            if (!entity.AddFinalizer(identifier))
            {
                return Task.FromResult(entity);
            }

            logger.LogInformation(
                """Added finalizer "{Finalizer}" on entity "{Kind}/{Name}".""",
                identifier,
                entity.Kind,
                entity.Name());
            return client.UpdateAsync(entity, token);
        };
    }
}
