// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Events;

/// <summary>
/// <para>
/// Publish a new event for <paramref name="entity"/>. Events are created in the same namespace. However,
/// if no namespace is provided (e.g. for cluster-wide entities), the "default" namespace is used.
/// </para>
/// <para>
/// This effectively creates a new <see cref="Corev1Event"/> or updates the <see cref="Corev1Event.Count"/> and <see cref="Corev1Event.LastTimestamp"/>
/// of the existing <see cref="Corev1Event"/>.
/// </para>
/// </summary>
/// <remarks>
/// Events have a hex encoded name of a SHA512 hash. For the delegate to update an event,
/// <paramref name="entity"/>, <paramref name="reason"/>, <paramref name="message"/> and <paramref name="type"/> must be the same.
/// </remarks>
/// <param name="entity">The entity that is involved with the event.</param>
/// <param name="reason">The reason string. This should be a machine readable reason string.</param>
/// <param name="message">A human readable string for the event.</param>
/// <param name="type">The <see cref="EventType"/> of the event.</param>
/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
/// <returns>A task that finishes when the event is created or updated.</returns>
/// <example>
/// Controller that fires a simple reconcile event on any entity it encounters.
/// Note that the publication of an event does not trigger another reconcile.
/// <code>
/// public class V1TestEntityController : IEntityController&lt;V1TestEntity&gt;
/// {
///     private readonly EventPublisher _eventPublisher;
///
///     public V1TestEntityController()
///     {
///         _eventPublisher = eventPublisher;
///     }
///
///     public async Task ReconcileAsync(V1TestEntity entity, CancellationToken token)
///     {
///         await _eventPublisher(entity, "Reconciled", "Entity was reconciled.", cancellationToken: token);
///     }
/// }
/// </code>
/// </example>
public delegate Task EventPublisher(
    IKubernetesObject<V1ObjectMeta> entity,
    string reason,
    string message,
    EventType type = EventType.Normal,
    CancellationToken cancellationToken = default);
