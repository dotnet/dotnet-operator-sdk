// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Defines the strategy used to decide whether a watch event should trigger reconciliation.
/// </summary>
public enum ReconcileStrategy
{
    /// <summary>
    /// Reconcile only when the entity's <c>metadata.generation</c> increases,
    /// which happens exclusively on spec changes.
    /// Status updates, label/annotation changes, and other metadata writes are ignored.
    /// This is the default strategy and matches the behaviour of most Kubernetes controllers.
    /// </summary>
    ByGeneration = 0,

    /// <summary>
    /// Reconcile whenever the entity's <c>metadata.resourceVersion</c> changes,
    /// which happens on every successful write to the API server regardless of which field changed
    /// (spec, status, labels, annotations, finalizers, etc.).
    /// Choose this strategy when your controller must react to changes outside the spec.
    /// </summary>
    ByResourceVersion = 1,
}
