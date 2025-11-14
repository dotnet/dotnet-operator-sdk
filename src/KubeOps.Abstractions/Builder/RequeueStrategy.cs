// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Defines the strategy for requeuing reconciliation events within the operator.
/// </summary>
public enum RequeueStrategy
{
    /// <summary>
    /// Represents an in-memory requeue strategy where reconciliation events
    /// are managed and requeued without external persistence or reliance on third-party systems.
    /// Suitable for scenarios requiring lightweight or transient processing.
    /// </summary>
    InMemory,

    /// <summary>
    /// Represents a custom requeue strategy where the logic for managing and
    /// handling reconciliation events is fully defined and implemented by the user.
    /// This provides maximum flexibility for scenarios with specific or complex requirements.
    /// </summary>
    Custom,
}
