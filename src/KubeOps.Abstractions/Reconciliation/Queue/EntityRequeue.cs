// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation.Queue;

/// <summary>
/// Injectable delegate for scheduling an entity to be requeued after a specified amount of time.
/// </summary>
/// <typeparam name="TEntity">The type of the Kubernetes entity being requeued.</typeparam>
/// <param name="entity">The entity instance that should be requeued.</param>
/// <param name="type">The type of operation for which the reconcile behavior should be performed.</param>
/// <param name="requeueIn">The duration to wait before triggering the next reconcile process.</param>
/// <param name="cancellationToken">A cancellation token to observe while waiting for the requeue duration.</param>
public delegate void EntityRequeue<in TEntity>(
    TEntity entity, RequeueType type, TimeSpan requeueIn, CancellationToken cancellationToken)
    where TEntity : IKubernetesObject<V1ObjectMeta>;
