// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Events;

/// <summary>
/// Factory interface for creating <see cref="Corev1Event"/> instances.
/// Implement this interface to customize the shape of events published by the operator.
/// </summary>
/// <remarks>
/// The default implementation of <see cref="EventPublisher"/> checks if an event with the same
/// unique name already exists, and either updates the existing event or creates a new one accordingly.
/// When updating an existing event, its <see cref="Corev1Event.Count"/> property is incremented and
/// <see cref="Corev1Event.LastTimestamp"/> is updated to the current time.
/// When creating a new event, <see cref="Corev1Event.Count"/> is set to 1 and <see cref="Corev1Event.FirstTimestamp"/>
/// and <see cref="Corev1Event.LastTimestamp"/> are set to the current time unless they are already set by the factory.
/// </remarks>
public interface IEventResourceFactory
{
    /// <summary>
    /// Creates a new <see cref="Corev1Event"/> for the given entity and event details.
    /// </summary>
    /// <param name="entity">The entity that is involved with the event.</param>
    /// <param name="reason">The reason string. This should be a machine readable reason string.</param>
    /// <param name="message">A human readable string for the event.</param>
    /// <param name="type">The <see cref="EventType"/> of the event.</param>
    /// <returns>A new <see cref="Corev1Event"/> instance.</returns>
    /// <remarks>
    /// The default implementation creates a unique event name by combining the entity's UID, name, namespace, reason,
    /// message, and type, and then hashing this combination to ensure it fits Kubernetes naming constraints.
    /// This means that events with the same reason, message, and type for the same entity will be considered the same
    /// event and will have their count incremented.
    /// </remarks>
    Corev1Event CreateEvent(IKubernetesObject<V1ObjectMeta> entity, string reason, string message, EventType type);
}
