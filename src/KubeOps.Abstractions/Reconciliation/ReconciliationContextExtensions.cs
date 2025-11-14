// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation;

/// <summary>
/// Provides extension methods for the <see cref="ReconciliationContext{TEntity}"/> class
/// to facilitate the identification of reconciliation trigger sources.
/// </summary>
public static class ReconciliationContextExtensions
{
    /// <summary>
    /// Determines if the reconciliation context was triggered by the Kubernetes API server.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes resource associated with the reconciliation context.</typeparam>
    /// <param name="reconciliationContext">The reconciliation context to check.</param>
    /// <returns>True if the reconciliation was triggered by the API server; otherwise, false.</returns>
    public static bool IsTriggeredByApiServer<TEntity>(this ReconciliationContext<TEntity> reconciliationContext)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => reconciliationContext.ReconciliationTriggerSource == ReconciliationTriggerSource.ApiServer;

    /// <summary>
    /// Determines if the reconciliation context was triggered by the operator.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes resource associated with the reconciliation context.</typeparam>
    /// <param name="reconciliationContext">The reconciliation context to check.</param>
    /// <returns>True if the reconciliation was triggered by the operator; otherwise, false.</returns>
    public static bool IsTriggeredByOperator<TEntity>(this ReconciliationContext<TEntity> reconciliationContext)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => reconciliationContext.ReconciliationTriggerSource == ReconciliationTriggerSource.Operator;
}
