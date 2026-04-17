// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Defines the strategy for queuing reconciliation events within the operator.
/// </summary>
public enum QueueStrategy
{
    /// <summary>
    /// Represents an in-memory queue strategy where reconciliation events
    /// are managed and queued without external persistence or reliance on third-party systems.
    /// Suitable for scenarios requiring lightweight or transient processing.
    /// </summary>
    InMemory,

    /// <summary>
    /// Represents a custom queue strategy where the logic for managing and
    /// handling reconciliation events is fully defined and implemented by the user.
    /// This provides maximum flexibility for scenarios with specific or complex requirements.
    /// </summary>
    Custom,
}
