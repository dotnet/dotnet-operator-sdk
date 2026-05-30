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
/// This service deliberately performs a <strong>hard stop</strong> on leadership loss: cancelling the
/// internal token aborts the dequeue loop as well as any reconciliation that is currently running. This
/// mirrors the behaviour of the wider Kubernetes operator ecosystem — controller-runtime
/// (Kubebuilder / Operator SDK) terminates the whole process when its lease is lost rather than draining
/// work gracefully.
/// </para>
/// <para>
/// Leader election does not guarantee strict mutual exclusion: clock skew, GC pauses or a slow API server
/// can leave an instance acting briefly after its lease has expired. That short transition overlap is
/// expected and is made safe by two properties the SDK already relies on:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <strong>Optimistic concurrency</strong> — concurrent writes to the same object are serialised by the
/// API server via <c>metadata.resourceVersion</c>; a stale write fails with HTTP 409 Conflict.
/// </description></item>
/// <item><description>
/// <strong>Level-triggered, idempotent reconciliation</strong> — a reconciler converges observed state
/// towards desired state, so an interrupted reconciliation is simply re-run by the new leader against the
/// current (possibly partial) state. The lease timing (<c>LeaseDuration &gt; RenewDeadline</c>) bounds the
/// overlap window.
/// </description></item>
/// </list>
/// <para>
/// References:
/// <list type="bullet">
/// <item><description>https://kubernetes.io/docs/concepts/architecture/leases/</description></item>
/// <item><description>https://pkg.go.dev/sigs.k8s.io/controller-runtime/pkg/manager#Options</description></item>
/// </list>
/// </para>
/// </remarks>
internal class LeaderAwareEntityQueueBackgroundService<TEntity>(
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
        logger)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Subscribe for leadership updates.");

        elector.OnStartedLeading += StartedLeading;
        elector.OnStoppedLeading += StoppedLeading;

        return elector.IsLeader() ? base.StartAsync(cancellationToken) : Task.CompletedTask;
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
        _ = base.StartAsync(CancellationToken.None);
    }

    private void StoppedLeading()
    {
        logger.LogInformation("This instance stopped leading, stopping queue processing.");

        // Hard stop: cancelling the internal token aborts the dequeue loop and any in-flight
        // reconciliation. See the class remarks for why interrupting running reconciliations is safe.
        _ = base.StopAsync(CancellationToken.None);
    }
}
