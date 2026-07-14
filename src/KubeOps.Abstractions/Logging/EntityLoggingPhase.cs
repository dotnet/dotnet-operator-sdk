// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Logging;

/// <summary>
/// Identifies the pipeline stage in which an entity logging scope is being created, allowing
/// enrichers to contribute different properties for a watch event than for a reconciliation.
/// </summary>
public enum EntityLoggingPhase
{
    /// <summary>The scope is created while ingesting a watch event from the API server.</summary>
    Watch,

    /// <summary>The scope is created while reconciling a queued entity.</summary>
    Reconcile,
}
