// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Reconciliation.Queue;

/// <summary>
/// Specifies the types of requeue operations that can occur on an entity.
/// </summary>
public enum RequeueType
{
    /// <summary>
    /// Indicates that an entity should be added and is scheduled for requeue.
    /// </summary>
    Added,

    /// <summary>
    /// Indicates that an entity has been modified and is scheduled for requeue.
    /// </summary>
    Modified,

    /// <summary>
    /// Indicates that an entity should be deleted and is scheduled for requeue.
    /// </summary>
    Deleted,
}
