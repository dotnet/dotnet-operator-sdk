// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;

using k8s;
using k8s.Models;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Marker for a hosted service that drains the <see cref="ITimedEntityQueue{TEntity}"/> for
/// <typeparamref name="TEntity"/> (i.e. the queue consumer).
/// </summary>
/// <typeparam name="TEntity">The type of the Kubernetes entity being reconciled.</typeparam>
/// <remarks>
/// The SDK's <see cref="EntityQueueBackgroundService{TEntity}"/> (and its leader-aware variant) implement
/// this. Its sole purpose is to let registration validation recognise a queue consumer: a custom consumer
/// supplied with <see cref="Abstractions.Builder.QueueStrategy.Custom"/> should implement this interface
/// (or derive from <see cref="EntityQueueBackgroundService{TEntity}"/>) so that validation can confirm a
/// consumer exists. It is not required at runtime — any hosted service can drain the queue — but without
/// it, registration validation cannot prove a consumer is present.
/// </remarks>
[SuppressMessage(
    "Major Code Smell",
    "S2326:Unused type parameters should be removed",
    Justification = "TEntity is a phantom type that ties the consumer marker to a specific entity; it is used by registration validation, not by interface members.")]
public interface IEntityQueueConsumer<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
}
