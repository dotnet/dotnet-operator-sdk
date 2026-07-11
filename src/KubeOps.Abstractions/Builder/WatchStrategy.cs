// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Defines how the operator creates watch connections to the Kubernetes API server
/// for the entities it manages.
/// </summary>
public enum WatchStrategy
{
    /// <summary>
    /// One watch connection per registered controller (the default). Each controller's label and
    /// field selectors are applied server-side, so the API server only delivers matching objects.
    /// This is the simplest and most efficient mode for a single controller per entity type or for
    /// disjoint selectors over large object sets. With multiple controllers for the same entity type,
    /// each controller maintains its own watch connection and deduplication cache; overlapping
    /// selectors cause events to be transferred and cached once per controller.
    /// </summary>
    PerController,

    /// <summary>
    /// One shared watch connection per entity type. Events are deduplicated once and then dispatched
    /// to every controller whose label selector matches the entity (evaluated client-side). Compared
    /// to <see cref="PerController"/> this reduces the number of API server connections and
    /// deduplication cache entries to one per entity type, independent of the controller count —
    /// beneficial when many controllers watch the same entity type or selectors overlap. The trade-off:
    /// when multiple controllers are registered for an entity type, the watch runs without a server-side
    /// label selector, so the operator receives all objects of that type. Controllers with a field
    /// selector keep a dedicated watch connection (field selectors cannot be evaluated client-side).
    /// </summary>
    SharedPerEntity,
}
