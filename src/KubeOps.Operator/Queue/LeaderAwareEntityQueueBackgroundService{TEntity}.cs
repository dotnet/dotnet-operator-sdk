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
using KubeOps.Operator.LeaderElection;
using KubeOps.Operator.Metrics;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

/// <summary>
/// A leadership-aware variant of <see cref="EntityQueueBackgroundService{TEntity}"/>. The queue is only
/// consumed while this instance holds leadership; when leadership is lost the queue's intake is suspended, the
/// dequeue loop is stopped and cancellation is <em>requested</em> for any in-flight reconciliation. This stops
/// the queue side promptly, but cannot force a non-cooperative reconciler that ignores its
/// <see cref="CancellationToken"/> to abort — see the remarks.
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
    LeaderElector elector,
    OperatorMetrics? metrics = null)
    : EntityQueueBackgroundService<TEntity>(
        activitySource,
        client,
        operatorSettings,
        queue,
        reconciler,
        logger,
        metrics), ILeaderAwareEntityQueueConsumer<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private LeaderElectionSubscription? _subscription;

    private ISuspendableEntityQueue? Gate => Queue as ISuspendableEntityQueue;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Subscribe for leadership updates.");

        _subscription ??= new(elector, StartedLeading, StoppedLeading);
        _subscription.Subscribe();

        if (Gate is null)
        {
            logger.LogWarning(
                "The configured queue ({QueueType}) does not implement {Capability}; leadership-loss " +
                "protection (queue clear and intake suspension) is disabled. A former leader may leave " +
                "queued work behind on a leadership transition.",
                Queue.GetType().Name,
                nameof(ISuspendableEntityQueue));
        }

        if (_subscription.IsLeader)
        {
            Gate?.ResumeIntake();
            return base.StartAsync(cancellationToken);
        }

        // Not leading yet: keep the intake gate closed so nothing accumulates work until leadership is held.
        logger.LogDebug("Starting as non-leader; intake gate kept closed until leadership is acquired.");
        Gate?.SuspendIntake();
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Unsubscribe from leadership updates.");
        _subscription?.Unsubscribe();

        // Always delegate to the base stop: it drains in-flight reconciliations, bounded by the host shutdown
        // token, so the processing loop is reliably awaited on host shutdown even when leadership was already lost.
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override void OnDisposing() => _subscription?.Unsubscribe();

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

        // This runs inside the elector's OnStoppedLeading callback, so the safety-critical stop must not be skipped
        // by a failure in the (best-effort) gate operations of a custom queue — and no exception may propagate into
        // the callback (it would be misattributed as a leadership-hold failure).
        //
        // Close the intake gate FIRST so nothing — including a still-running reconciler's RequeueAfter or an error
        // retry — can enqueue work during or after the stop.
        try
        {
            Gate?.SuspendIntake();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to suspend queue intake for {Entity} on leadership loss.", typeof(TEntity).Name);
        }

        // Cancel the dequeue loop and any in-flight reconciliation. Non-blocking on purpose: we no longer hold
        // leadership, so we abort and move on without waiting (the host-shutdown drain is a separate path).
        _ = RequestStopAsync();

        // Clear the work the former leader had already queued (best-effort).
        try
        {
            Gate?.Clear();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to clear the queue for {Entity} on leadership loss.", typeof(TEntity).Name);
        }
    }
}
