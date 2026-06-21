// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using k8s;
using k8s.LeaderElection;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Metrics;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Watcher;

public class LeaderAwareResourceWatcher<TEntity>(
    ActivitySource activitySource,
    ILogger<LeaderAwareResourceWatcher<TEntity>> logger,
    IFusionCacheProvider cacheProvider,
    ITimedEntityQueue<TEntity> entityQueue,
    OperatorSettings settings,
    IEntityLabelSelector<TEntity> labelSelector,
    IEntityFieldSelector<TEntity> fieldSelector,
    IKubernetesClient client,
    IHostApplicationLifetime hostApplicationLifetime,
    LeaderElector elector,
    OperatorMetrics? metrics = null)
    : ResourceWatcher<TEntity>(
        activitySource,
        logger,
        cacheProvider,
        entityQueue,
        settings,
        labelSelector,
        fieldSelector,
        client,
        metrics)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private bool _disposed;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Subscribe for leadership updates.");

        elector.OnStartedLeading += StartedLeading;
        elector.OnStoppedLeading += StoppedLeading;

        // Only start watching while leadership is actually held. The base watcher owns the single cancellation
        // source; StartedLeading/StoppedLeading restart and stop it on leadership transitions.
        return elector.IsLeader() ? base.StartAsync(cancellationToken) : Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Unsubscribe from leadership updates.");
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        elector.OnStartedLeading -= StartedLeading;
        elector.OnStoppedLeading -= StoppedLeading;

        // Always delegate to the base stop: it is a no-op when no watch task is running, so the watcher loop is
        // reliably awaited and torn down on host shutdown even when leadership was already lost — rather than
        // relying solely on the fire-and-forget StopAsync issued from the OnStoppedLeading callback.
        return base.StopAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        elector.OnStartedLeading -= StartedLeading;
        elector.OnStoppedLeading -= StoppedLeading;
        elector.Dispose();
        _disposed = true;

        base.Dispose(disposing);
    }

    private void StartedLeading()
    {
        logger.LogInformation("This instance started leading, starting watcher.");

        // The base watcher recreates its cancellation source when it was previously cancelled, so this
        // restarts the watch after a leadership loss. The token passed here is unused by the base watcher.
        base.StartAsync(CancellationToken.None);
    }

    private void StoppedLeading()
    {
        logger.LogInformation("This instance stopped leading, stopping watcher.");

        EntityCache.Clear();
        _ = base.StopAsync(hostApplicationLifetime.ApplicationStopped);
    }
}
