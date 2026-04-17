// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation;

/// <summary>
/// Represents the context for a reconciliation operation on a Kubernetes entity.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes resource being reconciled. Must implement <see cref="IKubernetesObject{V1ObjectMeta}"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// This record encapsulates all the contextual information required to perform a reconciliation operation,
/// including the entity being reconciled, the type of reconciliation operation to perform, and the source
/// that triggered the reconciliation.
/// </para>
/// <para>
/// Instances are immutable and should be created using the <see cref="CreateFor"/> factory method.
/// The context is passed to controller reconciliation methods to provide complete information about
/// the reconciliation request.
/// </para>
/// </remarks>
/// <seealso cref="ReconciliationType"/>
/// <seealso cref="ReconciliationTriggerSource"/>
public sealed record ReconciliationContext<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private ReconciliationContext(TEntity entity, ReconciliationType eventType, ReconciliationTriggerSource reconciliationTriggerSource)
    {
        Entity = entity;
        EventType = eventType;
        ReconciliationTriggerSource = reconciliationTriggerSource;
    }

    /// <summary>
    /// Gets the Kubernetes entity being reconciled.
    /// </summary>
    /// <value>
    /// The entity instance that requires reconciliation processing.
    /// </value>
    public TEntity Entity { get; }

    /// <summary>
    /// Gets the type of reconciliation operation to perform.
    /// </summary>
    /// <value>
    /// One of the enumeration values that indicates whether the entity was added, modified, or deleted.
    /// This determines which controller methods will be invoked during reconciliation.
    /// </value>
    /// <remarks>
    /// This property corresponds to Kubernetes watch event types and determines the reconciliation
    /// logic to be applied (e.g., creation, update, or deletion handling).
    /// </remarks>
    public ReconciliationType EventType { get; }

    /// <summary>
    /// Gets the source that triggered the reconciliation operation.
    /// </summary>
    /// <value>
    /// One of the enumeration values that indicates whether the reconciliation was triggered by
    /// the Kubernetes API server or by an internal operator operation.
    /// </value>
    /// <remarks>
    /// This property provides diagnostic context about the origin of the reconciliation request,
    /// which can be useful for logging, metrics, and debugging purposes.
    /// </remarks>
    public ReconciliationTriggerSource ReconciliationTriggerSource { get; }

    /// <summary>
    /// Creates a new reconciliation context for the specified entity and reconciliation parameters.
    /// </summary>
    /// <param name="entity">
    /// The Kubernetes entity to be reconciled.
    /// </param>
    /// <param name="eventType">
    /// One of the enumeration values that specifies the type of reconciliation operation to perform.
    /// </param>
    /// <param name="reconciliationTriggerSource">
    /// One of the enumeration values that specifies whether the reconciliation was triggered by
    /// the Kubernetes API server or by an internal operator operation.
    /// </param>
    /// <returns>
    /// A new reconciliation context instance containing the specified entity and reconciliation parameters.
    /// </returns>
    /// <remarks>
    /// This is the recommended way to create reconciliation context instances. The method ensures
    /// all required contextual information is properly initialized.
    /// </remarks>
    public static ReconciliationContext<TEntity> CreateFor(TEntity entity, ReconciliationType eventType, ReconciliationTriggerSource reconciliationTriggerSource)
        => new(entity, eventType, reconciliationTriggerSource);
}
