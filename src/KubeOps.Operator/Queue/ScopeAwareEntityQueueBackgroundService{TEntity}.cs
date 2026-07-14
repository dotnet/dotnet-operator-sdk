// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.LeaderElection;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Metrics;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

/// <summary>
/// A scope-aware variant of <see cref="EntityQueueBackgroundService{TEntity}"/> used with
/// <see cref="LeaderElectionType.Scoped"/>: queued entries are only reconciled for entities
/// the <see cref="ILeadershipScope"/> declares this instance responsible for.
/// </summary>
/// <typeparam name="TEntity">The type of the Kubernetes entity being managed.</typeparam>
public class ScopeAwareEntityQueueBackgroundService<TEntity>(
    ActivitySource activitySource,
    IKubernetesClient client,
    OperatorSettings operatorSettings,
    ITimedEntityQueue<TEntity> queue,
    IReconciler<TEntity> reconciler,
    IEntityReconcileCoordinator<TEntity> coordinator,
    ILogger<ScopeAwareEntityQueueBackgroundService<TEntity>> logger,
    ILeadershipScope leadershipScope,
    IEntityLoggingScopeFactory<TEntity> scopeFactory,
    OperatorMetrics? metrics = null)
    : EntityQueueBackgroundService<TEntity>(
        activitySource,
        client,
        operatorSettings,
        queue,
        reconciler,
        coordinator,
        logger,
        scopeFactory,
        metrics)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <inheritdoc/>
    protected override ValueTask<bool> ShouldReconcileAsync(
        TEntity entity, QueueEntry<TEntity> entry, CancellationToken cancellationToken) =>

        // Checked on the entity that will actually be reconciled (the current object for Added/Modified,
        // the delete snapshot for Deleted), so responsibility is decided on current state even when the
        // scope partitions by a mutable entity property.
        leadershipScope.IsResponsibleForAsync(entity, cancellationToken);
}
