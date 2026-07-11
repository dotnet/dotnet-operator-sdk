// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Reconciliation;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Watcher;

/// <summary>
/// Dispatches events of a shared watch connection (<c>WatchStrategy.SharedPerEntity</c>) to the controller
/// pipelines sharing it. Because the shared watch carries no server-side label selector and deduplicates
/// once per entity type, membership transitions (an object entering or leaving a pipeline's selector) are
/// evaluated <em>before</em> deduplication so a label-only change is never dropped — giving parity with a
/// dedicated server-side filtered watch (<c>WatchStrategy.PerController</c>), which delivers an
/// <c>Added</c> on entry and a <c>Deleted</c> on exit. Steady-state events (an object that keeps matching)
/// still go through the single shared deduplication.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type.</typeparam>
/// <param name="targets">The pipeline targets sharing the watch connection.</param>
/// <param name="logger">The logger of the owning watcher.</param>
internal sealed class SharedPipelineDispatcher<TEntity>(
    IReadOnlyList<SharedPipelineDispatcher<TEntity>.PipelineTarget> targets,
    ILogger logger)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// Processes a watch event: evaluates entry/exit transitions per pipeline (bypassing deduplication),
    /// then dispatches the steady-state event to still-matching members behind a single shared dedup
    /// decision, and finally records the shared dedup token once when anything was scheduled.
    /// </summary>
    /// <param name="eventType">The watch event type.</param>
    /// <param name="entity">The entity received from the shared watch stream.</param>
    /// <param name="dedup">The owning watcher's deduplication, consulted once for steady-state events.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the event has been dispatched.</returns>
    public async Task ProcessEventAsync(
        WatchEventType eventType,
        TEntity entity,
        ISharedWatchDedup<TEntity> dedup,
        CancellationToken cancellationToken)
    {
        var uid = entity.Uid();
        var isDelete = eventType == WatchEventType.Deleted;
        var anyEnqueued = false;
        var steadyDropped = false;

        // Snapshot the per-target match/membership state and handle transitions first, so a steady pass can
        // skip targets already served here. A target is "handled" when it received a transition event or is
        // not eligible for the steady pass.
        var matchesNow = new bool[targets.Count];
        var handled = new bool[targets.Count];

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var wasMember = target.IsMember(uid);

            bool matches;
            try
            {
                matches = !isDelete && await target.MatchesAsync(entity, cancellationToken);
            }
            catch (FormatException e)
            {
                logger.LogError(
                    e,
                    """The label selector of pipeline "{Pipeline}" could not be parsed for client-side matching; skipping dispatch to this pipeline.""",
                    target.PipelineKey);
                handled[i] = true;
                continue;
            }

            matchesNow[i] = matches;

            if (isDelete)
            {
                // A real delete is an exit for every current member; non-members have nothing to clean up.
                if (wasMember && await EnqueueAsync(target, entity, ReconciliationType.Deleted, cancellationToken))
                {
                    target.RemoveMember(uid);
                    anyEnqueued = true;
                }

                handled[i] = true;
            }
            else if (matches && !wasMember)
            {
                // Entry: mirror the Added a server-side filtered watch delivers when an object starts matching.
                if (await EnqueueAsync(target, entity, ReconciliationType.Added, cancellationToken))
                {
                    target.AddMember(uid);
                    anyEnqueued = true;
                }

                handled[i] = true;
            }
            else if (!matches && wasMember)
            {
                // Exit: mirror the Deleted a server-side filtered watch delivers when an object stops matching.
                if (await EnqueueAsync(target, entity, ReconciliationType.Deleted, cancellationToken))
                {
                    target.RemoveMember(uid);
                    anyEnqueued = true;
                }

                handled[i] = true;
            }

            // Remaining case: matches && wasMember (steady) — deferred to the dedup-gated pass below.
        }

        // Steady state: still-matching existing members, decided by a single shared dedup call.
        if (!isDelete && HasSteadyTargets(matchesNow, handled) &&
            !await dedup.IsDuplicateAsync(eventType, entity, cancellationToken))
        {
            var type = eventType.ToReconciliationType();
            for (var i = 0; i < targets.Count; i++)
            {
                if (!matchesNow[i] || handled[i])
                {
                    continue;
                }

                if (await EnqueueAsync(targets[i], entity, type, cancellationToken))
                {
                    anyEnqueued = true;
                }
                else
                {
                    steadyDropped = true;
                }
            }
        }

        if (isDelete)
        {
            if (anyEnqueued)
            {
                await dedup.RemoveDedupTokenAsync(entity, cancellationToken);
            }

            return;
        }

        // Advance the shared token only when **every** still-matching member accepted the steady event. If
        // any steady enqueue was dropped (e.g. a queue's intake was momentarily suspended during a leadership
        // transition), leave the token unchanged so the same generation/resourceVersion is re-dispatched to
        // the dropped member(s) on the next event or relist. Writing it here would let the shared dedup
        // swallow the event and the dropped member would never catch up. Entries/exits bypass the token, so a
        // dropped transition self-heals via membership regardless.
        if (anyEnqueued && !steadyDropped)
        {
            await dedup.RecordDedupTokenAsync(entity, cancellationToken);
        }
    }

    /// <summary>
    /// Clears every pipeline's membership. Called by the shared watcher before a full relist (a session
    /// starting from a null resource version), after which the replayed <c>Added</c> events rebuild
    /// membership; never called on a resumed reconnect, where membership must be preserved.
    /// </summary>
    public void ResetMembership()
    {
        foreach (var target in targets)
        {
            target.ClearMembership();
        }
    }

    private static bool HasSteadyTargets(bool[] matchesNow, bool[] handled)
    {
        for (var i = 0; i < matchesNow.Length; i++)
        {
            if (matchesNow[i] && !handled[i])
            {
                return true;
            }
        }

        return false;
    }

    private static Task<bool> EnqueueAsync(PipelineTarget target, TEntity entity, ReconciliationType type, CancellationToken cancellationToken) =>
        target.Queue.Enqueue(entity, type, ReconciliationTriggerSource.ApiServer, queueIn: TimeSpan.Zero, retryCount: 0, cancellationToken);

    /// <summary>
    /// One controller pipeline behind a shared watch connection: its queue, the client-side evaluation of
    /// its label selector, and the in-memory set of entity UIDs currently considered matching (membership).
    /// Parsed selector requirements are memoized per selector string. The shared watch loop is
    /// single-threaded, so neither the memoization nor the membership set needs locking.
    /// </summary>
    /// <param name="pipelineKey">The pipeline identity for diagnostics.</param>
    /// <param name="queue">The pipeline's queue.</param>
    /// <param name="labelSelector">The pipeline's label selector.</param>
    internal sealed class PipelineTarget(
        string pipelineKey,
        ITimedEntityQueue<TEntity> queue,
        IEntityLabelSelector<TEntity> labelSelector)
    {
        private readonly IClientSideEntitySelector<TEntity>? _clientSideSelector =
            labelSelector as IClientSideEntitySelector<TEntity>;

        private readonly HashSet<string> _members = [];

        private string? _parsedSelector;
        private IReadOnlyList<LabelSelectorMatcher.Requirement> _requirements = [];

        public string PipelineKey => pipelineKey;

        public ITimedEntityQueue<TEntity> Queue => queue;

        public bool IsMember(string uid) => _members.Contains(uid);

        public void AddMember(string uid) => _members.Add(uid);

        public void RemoveMember(string uid) => _members.Remove(uid);

        public void ClearMembership() => _members.Clear();

        public async ValueTask<bool> MatchesAsync(TEntity entity, CancellationToken cancellationToken)
        {
            if (_clientSideSelector is not null)
            {
                return await _clientSideSelector.MatchesAsync(entity, cancellationToken);
            }

            var selector = await labelSelector.GetLabelSelectorAsync(cancellationToken);
            if (!string.Equals(selector, _parsedSelector, StringComparison.Ordinal))
            {
                _requirements = LabelSelectorMatcher.Parse(selector);
                _parsedSelector = selector;
            }

            return LabelSelectorMatcher.Matches(_requirements, entity.Labels());
        }
    }
}
