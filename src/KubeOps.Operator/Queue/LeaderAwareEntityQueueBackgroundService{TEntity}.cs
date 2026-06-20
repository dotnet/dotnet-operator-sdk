// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using k8s;
using k8s.LeaderElection;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

/// <summary>
/// A leadership-aware variant of <see cref="EntityQueueBackgroundService{TEntity}"/>. The queue is only
/// consumed while this instance holds leadership; when leadership is lost the processing loop and any
/// in-flight reconciliations are cancelled immediately.
/// </summary>
/// <typeparam name="TEntity">The type of the Kubernetes entity being managed.</typeparam>
/// <remarks>
/// <para>
/// On leadership loss this service performs a <strong>hard stop</strong> combined with a queue gate: it
/// suspends the queue's intake, cancels the dequeue loop and any in-flight reconciliation, and clears the
/// queue. While the instance is not the leader the intake stays closed, so neither a still-running
/// reconciler's <c>RequeueAfter</c> nor an error retry can leave work behind. On re-acquiring leadership the
/// intake is reopened and the watcher re-lists the current state.
/// </para>
/// <para>
/// This protects the <em>queue</em> side only. KubeOps does <strong>not</strong> terminate the process on
/// leadership loss (unlike controller-runtime). It therefore cannot prevent a <strong>non-cooperative</strong>
/// reconciler that ignores the <see cref="CancellationToken"/> from performing external side effects while a
/// former leader. Reconciler implementations must honour cancellation and be idempotent. As a second line of
/// defence, concurrent writes to the same object are serialised by the API server via
/// <c>metadata.resourceVersion</c> (a stale write fails with HTTP 409 Conflict).
/// </para>
/// <para>
/// See https://kubernetes.io/docs/concepts/architecture/leases/ for leader-election semantics.
/// </para>
/// </remarks>
public class LeaderAwareEntityQueueBackgroundService<TEntity>(
    ActivitySource activitySource,
    IKubernetesClient client,
    OperatorSettings operatorSettings,
    ITimedEntityQueue<TEntity> queue,
    IReconciler<TEntity> reconciler,
    ILogger<LeaderAwareEntityQueueBackgroundService<TEntity>> logger,
    LeaderElector elector)
    : EntityQueueBackgroundService<TEntity>(
        activitySource,
        client,
        operatorSettings,
        queue,
        reconciler,
        logger), ILeaderAwareEntityQueueConsumer<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private ISuspendableEntityQueue? Gate => Queue as ISuspendableEntityQueue;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Subscribe for leadership updates.");

        elector.OnStartedLeading += StartedLeading;
        elector.OnStoppedLeading += StoppedLeading;

        if (Gate is null)
        {
            logger.LogWarning(
                "The configured queue ({QueueType}) does not implement {Capability}; leadership-loss " +
                "protection (queue clear and intake suspension) is disabled. A former leader may leave " +
                "queued work behind on a leadership transition.",
                Queue.GetType().Name,
                nameof(ISuspendableEntityQueue));
        }

        if (elector.IsLeader())
        {
            Gate?.ResumeIntake();
            return base.StartAsync(cancellationToken);
        }

        // Not leading yet: keep the intake gate closed so nothing accumulates work until leadership is held.
        Gate?.SuspendIntake();
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Unsubscribe from leadership updates.");

        elector.OnStartedLeading -= StartedLeading;
        elector.OnStoppedLeading -= StoppedLeading;

        return elector.IsLeader() ? base.StopAsync(cancellationToken) : Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            elector.OnStartedLeading -= StartedLeading;
            elector.OnStoppedLeading -= StoppedLeading;
        }

        base.Dispose(disposing);
    }

    private void StartedLeading()
    {
        logger.LogInformation("This instance started leading, starting queue processing.");

        // Open the intake gate before producers run. This service is registered before the watcher
        // (see OperatorBuilder.AddController), so its OnStartedLeading runs first and the gate is open
        // by the time the watcher starts enqueuing.
        Gate?.ResumeIntake();
        _ = base.StartAsync(CancellationToken.None);
    }

    private void StoppedLeading()
    {
        logger.LogInformation("This instance stopped leading, stopping queue processing.");

        // Close the intake gate FIRST so nothing — including a still-running reconciler's RequeueAfter or
        // an error retry — can enqueue work during or after the stop. Then cancel the dequeue loop and any
        // in-flight reconciliation, and clear the work the former leader had already queued.
        Gate?.SuspendIntake();
        _ = base.StopAsync(CancellationToken.None);
        Gate?.Clear();
    }
}
