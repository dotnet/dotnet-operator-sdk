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
/// Dispatches events of a shared watch connection (<c>WatchStrategy.SharedPerEntity</c>) to all
/// controller pipelines whose label selector matches the entity. Selector matching happens
/// client-side: either via <see cref="IClientSideEntitySelector{TEntity}"/> when the selector
/// implements it, or by parsing the selector string with <see cref="LabelSelectorMatcher"/>.
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
    /// Dispatches a watch event to every matching pipeline's queue.
    /// </summary>
    /// <param name="eventType">The watch event type.</param>
    /// <param name="entity">The entity received from the watch stream.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when the event was scheduled on at least one pipeline's queue.</returns>
    public async Task<bool> DispatchAsync(WatchEventType eventType, TEntity entity, CancellationToken cancellationToken)
    {
        var enqueued = false;
        foreach (var target in targets)
        {
            bool matches;
            try
            {
                matches = await target.MatchesAsync(entity, cancellationToken);
            }
            catch (FormatException e)
            {
                logger.LogError(
                    e,
                    """The label selector of pipeline "{Pipeline}" could not be parsed for client-side matching; skipping dispatch to this pipeline.""",
                    target.PipelineKey);
                continue;
            }

            if (!matches)
            {
                continue;
            }

            enqueued |= await target.Queue.Enqueue(
                entity,
                eventType.ToReconciliationType(),
                ReconciliationTriggerSource.ApiServer,
                queueIn: TimeSpan.Zero,
                retryCount: 0,
                cancellationToken);
        }

        return enqueued;
    }

    /// <summary>
    /// One controller pipeline behind a shared watch connection: its queue plus the client-side
    /// evaluation of its label selector. Parsed selector requirements are memoized per selector string
    /// (selectors may change between events; the watch loop is single-threaded, so no locking is needed).
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

        private string? _parsedSelector;
        private IReadOnlyList<LabelSelectorMatcher.Requirement> _requirements = [];

        public string PipelineKey => pipelineKey;

        public ITimedEntityQueue<TEntity> Queue => queue;

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
