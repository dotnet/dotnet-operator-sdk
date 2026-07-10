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
/// <see cref="LeaderElectionType.Scoped"/>: events are only processed for namespaces the
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
    private readonly OperatorSettings _settings = settings;
    private readonly IEntityLabelSelector<TEntity> _labelSelector = labelSelector;
    private readonly IKubernetesClient _client = client;

    private CancellationTokenSource? _resyncCts;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Subscribe for leadership scope updates.");

        _resyncCts ??= new CancellationTokenSource();
        leadershipScope.ScopeChanged += OnScopeChanged;

        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Unsubscribe from leadership scope updates.");

        leadershipScope.ScopeChanged -= OnScopeChanged;
        _resyncCts?.Cancel();

        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task OnEventAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken)
    {
        if (!await leadershipScope.IsResponsibleForAsync(entity.Namespace(), cancellationToken))
        {
            logger
                .LogTrace(
                    """This instance is not responsible for the namespace of "{Identifier}". Skip event.""",
                    entity.ToIdentifierString());
            return;
        }

        await base.OnEventAsync(eventType, entity, cancellationToken);
    }

    /// <inheritdoc/>
    protected override void OnDisposing()
    {
        leadershipScope.ScopeChanged -= OnScopeChanged;
        _resyncCts?.Cancel();
        _resyncCts?.Dispose();
    }

    private void OnScopeChanged(LeadershipScopeChange change)
    {
        if (change.AcquiredNamespaces.Count == 0)
        {
            return;
        }

        // Fire-and-forget on purpose: this runs inside the scope implementation's change callback,
        // which must not block on API calls. Errors are handled and logged inside the resync.
        _ = ResyncAcquiredNamespacesAsync(change.AcquiredNamespaces, _resyncCts?.Token ?? CancellationToken.None);
    }

    private async Task ResyncAcquiredNamespacesAsync(
        IReadOnlyCollection<string> namespaces,
        CancellationToken cancellationToken)
    {
        foreach (var @namespace in namespaces)
        {
            // The watch is limited to a single namespace; acquired namespaces outside of it are
            // never delivered by the watch and must not be resynced either.
            if (_settings.Namespace is not null && _settings.Namespace != @namespace)
            {
                continue;
            }

            try
            {
                logger.LogInformation(
                    "Acquired responsibility for namespace {Namespace}, re-listing {ResourceType}.",
                    @namespace,
                    typeof(TEntity).Name);

                var entities = await _client.ListAsync<TEntity>(
                    @namespace,
                    await _labelSelector.GetLabelSelectorAsync(cancellationToken),
                    cancellationToken);

                foreach (var entity in entities)
                {
                    // Runs through the regular event path: the responsibility check (the scope may
                    // have changed again meanwhile) and the deduplication cache (entities already
                    // reconciled in their current state are skipped).
                    await OnEventAsync(WatchEventType.Modified, entity, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(
                    e,
                    "Failed to re-list {ResourceType} in acquired namespace {Namespace}. " +
                    "Changes made while not responsible are picked up with the next watch re-list.",
                    typeof(TEntity).Name,
                    @namespace);
            }
        }
    }
}
