// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using k8s;
using k8s.LeaderElection;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Watcher;

internal sealed class LeaderAwareResourceWatcher<TEntity>(
    ActivitySource activitySource,
    ILogger<LeaderAwareResourceWatcher<TEntity>> logger,
    IReconciler<TEntity> reconciler,
    OperatorSettings settings,
    IEntityLabelSelector<TEntity> labelSelector,
    IKubernetesClient client,
    IHostApplicationLifetime hostApplicationLifetime,
    LeaderElector elector)
    : ResourceWatcher<TEntity>(
        activitySource,
        logger,
        reconciler,
        settings,
        labelSelector,
        client)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private CancellationTokenSource _cts = new();
    private bool _disposed;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Subscribe for leadership updates.");

        elector.OnStartedLeading += StartedLeading;
        elector.OnStoppedLeading += StoppedLeading;

        if (elector.IsLeader())
        {
            using CancellationTokenSource linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            await base.StartAsync(linkedCancellationTokenSource.Token);
        }
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

        return elector.IsLeader() ? base.StopAsync(cancellationToken) : Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _cts.Dispose();
        elector.Dispose();
        _disposed = true;

        base.Dispose(disposing);
    }

    private void StartedLeading()
    {
        logger.LogInformation("This instance started leading, starting watcher.");

        if (_cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = new();
        }

        base.StartAsync(_cts.Token);
    }

    private void StoppedLeading()
    {
        _cts.Cancel();

        logger.LogInformation("This instance stopped leading, stopping watcher.");

        // Stop the base implementation using the 'ApplicationStopped' cancellation token.
        // The cancellation token should only be marked as canceled when the stop should no longer be graceful.
        base.StopAsync(hostApplicationLifetime.ApplicationStopped).Wait();
    }
}
