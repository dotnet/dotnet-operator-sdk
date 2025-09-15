// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Specifies the types of leader election mechanisms to be used in distributed systems or workloads.
/// </summary>
public enum LeaderElectionType
{
    /// <summary>
    /// Represents the absence of a leader election mechanism.
    /// This option is used when no leader election is required, and all instances
    /// are expected to operate without coordination or exclusivity.
    /// </summary>
    None = 0,

    /// <summary>
    /// Represents the leader election mechanism where only a single instance of the application
    /// assumes the leader role at any given time. This is used to coordinate operations
    /// that require exclusivity or to manage shared resources in distributed systems.
    /// </summary>
    Single = 1,

    /// <summary>
    /// Represents a custom leader election mechanism determined by the user.
    /// This option allows the integration of user-defined logic for handling
    /// leader election, enabling tailored coordination strategies beyond the
    /// provided defaults.
    /// </summary>
    Custom = 2,
}
