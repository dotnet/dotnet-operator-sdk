// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.LeaderElection;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Metrics;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.Logging;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Watcher;

/// <summary>
/// A scope-aware variant of <see cref="ResourceWatcher{TEntity}"/> used with
/// <see cref="LeaderElectionType.Scoped"/>: events are only processed for entities the
/// <see cref="ILeadershipScope"/> declares this instance responsible for.
/// </summary>
/// <typeparam name="TEntity">The type of the Kubernetes entity being watched.</typeparam>
public class ScopeAwareResourceWatcher<TEntity>(
    ActivitySource activitySource,
    ILogger<ScopeAwareResourceWatcher<TEntity>> logger,
    IFusionCacheProvider cacheProvider,
    ITimedEntityQueue<TEntity> entityQueue,
    OperatorSettings settings,
    IEntityLabelSelector<TEntity> labelSelector,
    IEntityFieldSelector<TEntity> fieldSelector,
    IKubernetesClient client,
    ILeadershipScope leadershipScope,
    string cachePartition = "",
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
        cachePartition,
        metrics)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly OperatorSettings _settings = settings;
    private readonly IEntityFieldSelector<TEntity> _fieldSelector = fieldSelector;
    private readonly IEntityLabelSelector<TEntity> _labelSelector = labelSelector;
    private readonly IKubernetesClient _client = client;

    // Serializes all event processing: the watch loop and the scope resync both funnel through
    // OnEventAsync, and downstream consumers (in particular the shared pipeline dispatcher)
    // assume a single caller.
    private readonly SemaphoreSlim _eventLock = new(1, 1);

    private readonly object _resyncGate = new();

    private CancellationTokenSource? _resyncCts;
    private Task _resyncTask = Task.CompletedTask;
    private bool _resyncRunning;
    private bool _resyncRequested;
    private bool _resyncStopped;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Subscribe for leadership scope updates.");

        // A previous StopAsync cancels the source; a restarted service needs a fresh one.
        if (_resyncCts is null || _resyncCts.IsCancellationRequested)
        {
            _resyncCts?.Dispose();
            _resyncCts = new CancellationTokenSource();
        }

        lock (_resyncGate)
        {
            _resyncStopped = false;
        }

        leadershipScope.ScopeChanged += OnScopeChanged;

        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Unsubscribe from leadership scope updates.");

        leadershipScope.ScopeChanged -= OnScopeChanged;
        if (_resyncCts is not null)
        {
            await _resyncCts.CancelAsync();
        }

        await Task.WhenAll(base.StopAsync(cancellationToken), StopResyncAndGetTask());
    }

    /// <inheritdoc/>
    protected sealed override async Task OnEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken)
    {
        await _eventLock.WaitAsync(cancellationToken);
        try
        {
            if (!await leadershipScope.IsResponsibleForAsync(entity, cancellationToken))
            {
                logger
                    .LogTrace(
                        """This instance is not responsible for "{Identifier}". Skip event.""",
                        entity.ToIdentifierString());
                return;
            }

            await OnScopedEventAsync(eventType, entity, cancellationToken);
        }
        finally
        {
            _eventLock.Release();
        }
    }

    /// <summary>
    /// Processes an event this instance is responsible for. Defaults to the regular event
    /// handling of <see cref="ResourceWatcher{TEntity}"/>.
    /// </summary>
    /// <param name="eventType">One of the enumeration values that specifies the type of watch event.</param>
    /// <param name="entity">The Kubernetes entity that triggered the watch event.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous event handling operation.</returns>
    protected virtual Task OnScopedEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken)
        => base.OnEventAsync(eventType, entity, cancellationToken);

    /// <inheritdoc/>
    protected override void OnDisposing()
    {
        // Pre-drain cleanup only: no scope callback may start a new resync (unsubscribing alone
        // does not stop a callback whose invocation list was already copied - the stop flag does),
        // and the running resync is cancelled. The synchronization primitives stay alive until the
        // loops and the resync are drained (see DisposeManagedResourcesAsync).
        leadershipScope.ScopeChanged -= OnScopeChanged;
        _ = StopResyncAndGetTask();
        _resyncCts?.Cancel();
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeManagedResourcesAsync()
    {
        // The watch loops are already drained; drain the fire-and-forget resync too before the
        // synchronization primitives and (via base) the Kubernetes client are released.
        await StopResyncAndGetTask();

        _resyncCts?.Dispose();
        _eventLock.Dispose();
        await base.DisposeManagedResourcesAsync();
    }

    /// <inheritdoc/>
    protected override void DisposeManagedResources()
    {
        // Synchronous fallback: like the base class, this path cannot await the drain. The
        // container disposes via IAsyncDisposable when available, so this is best-effort only.
        _resyncCts?.Dispose();
        _eventLock.Dispose();
        base.DisposeManagedResources();
    }

    private void OnScopeChanged()
    {
        // Coalesce: at most one resync runs at a time; further signals while it is running are
        // folded into a single follow-up pass. Fire-and-forget on purpose: this runs inside the
        // scope implementation's change callback, which must not block on API calls. Errors are
        // handled and logged inside the resync.
        lock (_resyncGate)
        {
            // A callback whose invocation started before StopAsync/dispose unsubscribed may arrive
            // here after the drain snapshot was taken; the stop flag (set under the same lock as
            // the snapshot) keeps it from starting a resync that would escape the drain.
            if (_resyncStopped)
            {
                return;
            }

            _resyncRequested = true;
            if (_resyncRunning)
            {
                return;
            }

            _resyncRunning = true;
            _resyncTask = RunResyncAsync(_resyncCts?.Token ?? CancellationToken.None);
        }
    }

    private Task StopResyncAndGetTask()
    {
        // Setting the stop flag and snapshotting the task under one lock makes "no new resyncs"
        // and the drain target a single atomic invariant.
        lock (_resyncGate)
        {
            _resyncStopped = true;
            _resyncRequested = false;
            return _resyncTask;
        }
    }

    private async Task RunResyncAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_resyncGate)
            {
                if (!_resyncRequested)
                {
                    _resyncRunning = false;
                    return;
                }

                _resyncRequested = false;
            }

            await ResyncAsync(cancellationToken);
        }
    }

    private async Task ResyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Leadership scope changed, re-listing {ResourceType} to pick up newly acquired entities.",
                typeof(TEntity).Name);

            var entities = await _client.ListAsync<TEntity>(
                _settings.Namespace,
                await _labelSelector.GetLabelSelectorAsync(cancellationToken),
                await _fieldSelector.GetFieldSelectorAsync(cancellationToken),
                cancellationToken);

            foreach (var entity in entities)
            {
                // Runs through the regular event path: the responsibility check and the
                // deduplication cache (entities already reconciled in their current state are
                // skipped).
                await OnEventAsync(WatchEventType.Modified, entity, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop during the resync; the re-list is abandoned on purpose.
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                "Failed to re-list {ResourceType} after a leadership scope change. Changes made " +
                "while not responsible are picked up with the next watch re-list.",
                typeof(TEntity).Name);
        }
    }
}
