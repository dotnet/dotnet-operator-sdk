// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Operator.Logging;

/// <summary>
/// Creates consistently enriched logging scopes for watch events and reconciliations of
/// <typeparamref name="TEntity"/>.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type the factory creates scopes for.</typeparam>
public interface IEntityLoggingScopeFactory<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>Creates a logging scope for a Kubernetes watch event.</summary>
    /// <param name="eventType">The watch event type.</param>
    /// <param name="entity">The entity associated with the event.</param>
    /// <returns>The enriched logging scope.</returns>
    EntityLoggingScope CreateFor(WatchEventType eventType, TEntity entity);

    /// <summary>Creates a logging scope for a reconciliation.</summary>
    /// <param name="eventType">The reconciliation operation type.</param>
    /// <param name="reconciliationTriggerSource">The source that triggered the reconciliation.</param>
    /// <param name="entity">The entity being reconciled.</param>
    /// <returns>The enriched logging scope.</returns>
    EntityLoggingScope CreateFor(
        ReconciliationType eventType,
        ReconciliationTriggerSource reconciliationTriggerSource,
        TEntity entity);
}
