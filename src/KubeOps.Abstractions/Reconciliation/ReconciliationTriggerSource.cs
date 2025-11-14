// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Reconciliation;

/// <summary>
/// Defines the source that triggered the reconciliation process in the Kubernetes operator.
/// Used to identify which component or mechanism initiated the reconciliation cycle.
/// </summary>
public enum ReconciliationTriggerSource
{
    /// <summary>
    /// Represents a reconciliation trigger initiated by the Kubernetes API server.
    /// This source typically implies that the operator has been informed about
    /// a resource event (e.g., creation, modification, deletion) via API server
    /// notifications or resource watches.
    /// </summary>
    ApiServer,

    /// <summary>
    /// Represents a reconciliation trigger initiated directly by the operator.
    /// This source indicates that the reconciliation process was started internally
    /// by the operator, such as during a scheduled task or an operator-specific event.
    /// </summary>
    Operator,
}
