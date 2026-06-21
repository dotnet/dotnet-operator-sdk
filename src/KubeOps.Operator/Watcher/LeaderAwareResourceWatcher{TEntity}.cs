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
using KubeOps.Operator.LeaderElection;
using KubeOps.Operator.Metrics;
using KubeOps.Operator.Queue;

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
    private LeaderElectionSubscription? _subscription;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Subscribe for leadership updates.");

        _subscription ??= new(elector, StartedLeading, StoppedLeading);
        _subscription.Subscribe();

        // Only start watching while leadership is actually held. StartedLeading/StoppedLeading restart and stop
        // the base watch loop on leadership transitions.
        return _subscription.IsLeader ? base.StartAsync(cancellationToken) : Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Unsubscribe from leadership updates.");
        _subscription?.Unsubscribe();

        // Always delegate to the base stop: it drains the watch loop, bounded by the host shutdown token, so the
        // watcher is reliably awaited and torn down on host shutdown even when leadership was already lost.
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override void OnDisposing() => _subscription?.Unsubscribe();

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

        // RequestStopAsync is non-blocking on purpose: we no longer hold leadership, so we abort (cancel) the
        // watch and move on without waiting (the host-shutdown drain is a separate path).
        EntityCache.Clear();
        _ = RequestStopAsync();
    }
}
