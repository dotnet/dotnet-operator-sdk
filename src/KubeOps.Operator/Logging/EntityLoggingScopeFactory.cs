// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Logging;
using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Operator.Logging;

internal sealed class EntityLoggingScopeFactory<TEntity>(
    EntityLoggingScopeEnricherPipeline<TEntity> enrichers)
    : IEntityLoggingScopeFactory<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    public EntityLoggingScope CreateFor(WatchEventType eventType, TEntity entity)
        => EntityLoggingScope.Create(
            eventType.ToString(),
            ReconciliationTriggerSource.ApiServer,
            entity,
            EntityLoggingPhase.Watch,
            enrichers);

    public EntityLoggingScope CreateFor(
        ReconciliationType eventType,
        ReconciliationTriggerSource reconciliationTriggerSource,
        TEntity entity)
        => EntityLoggingScope.Create(
            eventType.ToString(),
            reconciliationTriggerSource,
            entity,
            EntityLoggingPhase.Reconcile,
            enrichers);
}
