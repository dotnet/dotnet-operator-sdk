// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation;

/// <summary>
/// Represents the context for the reconciliation process.
/// This class contains information about the entity to be reconciled and
/// the source that triggered the reconciliation process.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes resource being reconciled. Must implement
/// <see cref="IKubernetesObject{V1ObjectMeta}"/>.
/// </typeparam>
public sealed record ReconciliationContext<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private ReconciliationContext(TEntity entity, WatchEventType eventType, ReconciliationTriggerSource reconciliationTriggerSource)
    {
        Entity = entity;
        EventType = eventType;
        ReconciliationTriggerSource = reconciliationTriggerSource;
    }

    /// <summary>
    /// Represents the Kubernetes entity involved in the reconciliation process.
    /// </summary>
    public TEntity Entity { get; }

    /// <summary>
    /// Specifies the type of Kubernetes watch event that triggered the reconciliation process.
    /// This property provides information about the nature of the change detected
    /// within the Kubernetes resource, such as addition, modification, or deletion.
    /// </summary>
    public WatchEventType EventType { get; }

    /// <summary>
    /// Specifies the source that initiated the reconciliation process.
    /// </summary>
    public ReconciliationTriggerSource ReconciliationTriggerSource { get; }

    /// <summary>
    /// Creates a new instance of <see cref="ReconciliationContext{TEntity}"/> from an API server event.
    /// </summary>
    /// <param name="entity">
    /// The Kubernetes entity associated with the reconciliation context.
    /// </param>
    /// <param name="eventType">
    /// The type of watch event that triggered the context creation.
    /// </param>
    /// <returns>
    /// A new <see cref="ReconciliationContext{TEntity}"/> instance representing the reconciliation context
    /// for the specified entity and event type, triggered by the API server.
    /// </returns>
    public static ReconciliationContext<TEntity> CreateFromApiServerEvent(TEntity entity, WatchEventType eventType)
        => new(entity, eventType, ReconciliationTriggerSource.ApiServer);

    /// <summary>
    /// Creates a new instance of <see cref="ReconciliationContext{TEntity}"/> from an operator-driven event.
    /// </summary>
    /// <param name="entity">
    /// The Kubernetes entity associated with the reconciliation context.
    /// </param>
    /// <param name="eventType">
    /// The type of watch event that triggered the context creation.
    /// </param>
    /// <returns>
    /// A new <see cref="ReconciliationContext{TEntity}"/> instance representing the reconciliation context
    /// for the specified entity and event type, triggered by the operator.
    /// </returns>
    public static ReconciliationContext<TEntity> CreateFromOperatorEvent(TEntity entity, WatchEventType eventType)
        => new(entity, eventType, ReconciliationTriggerSource.Operator);
}
