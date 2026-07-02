// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;

using k8s;
using k8s.Models;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Marker for a queue consumer that is leadership-aware: it gates the queue on leadership transitions by
/// driving <see cref="ISuspendableEntityQueue"/> (suspend intake and clear on leadership loss, resume on
/// re-acquisition).
/// </summary>
/// <typeparam name="TEntity">The type of the Kubernetes entity being reconciled.</typeparam>
/// <remarks>
/// The SDK's <see cref="LeaderAwareEntityQueueBackgroundService{TEntity}"/> implements this. With
/// <see cref="Abstractions.Builder.LeaderElectionType.Single"/> a queue capable of gating
/// (<see cref="ISuspendableEntityQueue"/>) is not enough — a consumer must actually drive that gate.
/// Registration validation therefore requires the consumer to implement this interface under
/// <c>Single</c>. A custom consumer supplied with <see cref="Abstractions.Builder.QueueStrategy.Custom"/>
/// should implement it (or derive from <see cref="LeaderAwareEntityQueueBackgroundService{TEntity}"/>).
/// Like all such markers it declares intent; it cannot prove the consumer actually calls the gate methods.
/// </remarks>
[SuppressMessage(
    "Major Code Smell",
    "S2326:Unused type parameters should be removed",
    Justification = "TEntity is a phantom type that ties the consumer marker to a specific entity; it is used by registration validation, not by interface members.")]
public interface ILeaderAwareEntityQueueConsumer<TEntity> : IEntityQueueConsumer<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
}
