// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Reconciliation;

/// <summary>
/// Specifies the type of reconciliation operation to perform on a Kubernetes entity.
/// </summary>
/// <remarks>
/// <para>
/// This enum defines the type of reconciliation that should be executed, corresponding to
/// the lifecycle events of Kubernetes resources. The reconciliation type determines which
/// controller methods are invoked and how the entity is processed.
/// </para>
/// <para>
/// The source of the reconciliation request can be either the Kubernetes API server
/// (via ResourceWatcher observing watch events) or an internal operation (error retry,
/// conflict retry, or periodic requeue), but both use the same reconciliation type
/// to specify how the entity should be handled.
/// </para>
/// </remarks>
public enum ReconciliationType
{
    /// <summary>
    /// Indicates that an entity was added and requires initial reconciliation.
    /// </summary>
    /// <remarks>
    /// This reconciliation type is used when a new Kubernetes resource is created.
    /// The reconciler should handle initial setup, resource creation, and initialization logic.
    /// Typically invokes the controller's create/add methods.
    /// </remarks>
    Added,

    /// <summary>
    /// Indicates that an entity was modified and requires update reconciliation.
    /// </summary>
    /// <remarks>
    /// This reconciliation type is used when an existing Kubernetes resource is updated.
    /// The reconciler should handle updates and ensure the desired state matches the actual state.
    /// Typically invokes the controller's modify/update methods.
    /// </remarks>
    Modified,

    /// <summary>
    /// Indicates that an entity was deleted and requires deletion reconciliation.
    /// </summary>
    /// <remarks>
    /// This reconciliation type is used when a Kubernetes resource is deleted.
    /// The reconciler should handle finalizer logic, cleanup of associated resources,
    /// and proper resource decommissioning. Typically invokes the controller's delete methods.
    /// </remarks>
    Deleted,
}
