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
    private ReconciliationContext(TEntity entity, ReconciliationTriggerSource reconciliationTriggerSource)
    {
        Entity = entity;
        ReconciliationTriggerSource = reconciliationTriggerSource;
    }

    /// <summary>
    /// Represents the Kubernetes entity involved in the reconciliation process.
    /// </summary>
    public TEntity Entity { get; }

    /// <summary>
    /// Specifies the source that initiated the reconciliation process.
    /// </summary>
    public ReconciliationTriggerSource ReconciliationTriggerSource { get; }

    /// <summary>
    /// Creates a new instance of the <see cref="ReconciliationContext{TEntity}"/> class
    /// using an event triggered by the Kubernetes API server as the source of reconciliation.
    /// </summary>
    /// <param name="entity">
    /// The Kubernetes resource instance that is being reconciled. This must implement
    /// <see cref="IKubernetesObject{V1ObjectMeta}"/>.
    /// </param>
    /// <returns>
    /// A new instance of <see cref="ReconciliationContext{TEntity}"/> with
    /// <see cref="ReconciliationTriggerSource.ApiServer"/> as the trigger source.
    /// </returns>
    public static ReconciliationContext<TEntity> CreateFromApiServerEvent(TEntity entity)
        => new(entity, ReconciliationTriggerSource.ApiServer);

    /// <summary>
    /// Creates a new instance of the <see cref="ReconciliationContext{TEntity}"/> class
    /// using an event triggered by the operator as the source of reconciliation.
    /// </summary>
    /// <param name="entity">
    /// The Kubernetes resource instance that is being reconciled. This must implement
    /// <see cref="IKubernetesObject{V1ObjectMeta}"/>.
    /// </param>
    /// <returns>
    /// A new instance of <see cref="ReconciliationContext{TEntity}"/> with
    /// <see cref="ReconciliationTriggerSource.Operator"/> as the trigger source.
    /// </returns>
    public static ReconciliationContext<TEntity> CreateFromOperatorEvent(TEntity entity)
        => new(entity, ReconciliationTriggerSource.Operator);
}
