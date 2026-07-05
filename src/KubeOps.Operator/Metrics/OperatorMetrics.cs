// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace KubeOps.Operator.Metrics;

/// <summary>
/// Owns the operator's OpenTelemetry instruments. A single instance is registered as a singleton
/// and shared across all entity types; the <c>kubeops.entity.type</c> tag distinguishes measurements per
/// watched resource.
/// </summary>
/// <remarks>
/// <para>
/// The underlying <see cref="Meter"/> is named after the operator (see <c>OperatorSettings.Name</c>),
/// matching the <see cref="System.Diagnostics.ActivitySource"/> name so a single identifier configures
/// both tracing and metrics. Recording is virtually free when no listener/exporter is attached.
/// </para>
/// <para>
/// The <see cref="Meter"/> is created through and owned by the <see cref="IMeterFactory"/>; it is
/// therefore disposed by the factory (the DI container) and intentionally not disposed here.
/// </para>
/// </remarks>
public sealed class OperatorMetrics
{
    private const string EntityTypeTag = "kubeops.entity.type";

    private readonly Counter<long> _queueEnqueued;
    private readonly Counter<long> _queueRequeued;
    private readonly Counter<long> _queueDiscarded;
    private readonly Counter<long> _reconciliationTotal;
    private readonly Histogram<double> _reconciliationDuration;
    private readonly Counter<long> _watcherEvents;
    private readonly Counter<long> _watcherReconnections;

    // Depth providers per entity type, observed by a single shared gauge. Using one instrument for
    // all entity types (rather than one per closed generic queue) avoids duplicate-instrument
    // registration warnings from the OpenTelemetry SDK. An entity type may have several providers
    // (one per controller pipeline); their depths are summed into one measurement per entity type.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueueDepthProvider>> _queueDepthProviders = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="OperatorMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">The factory used to create the underlying <see cref="Meter"/>.</param>
    /// <param name="meterName">The meter name; should match the operator name.</param>
    public OperatorMetrics(IMeterFactory meterFactory, string meterName)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(new MeterOptions(meterName)
        {
            Version = typeof(OperatorMetrics).Assembly.GetName().Version?.ToString(),
        });

        meter.CreateObservableGauge(
            "kubeops.operator.queue.depth",
            ObserveQueueDepth,
            "{items}",
            "Current number of entities in the queue, split by scheduled and ready state.");

        _queueEnqueued = meter.CreateCounter<long>(
            "kubeops.operator.queue.enqueued",
            "{items}",
            "Total number of entities enqueued for reconciliation.");
        _queueRequeued = meter.CreateCounter<long>(
            "kubeops.operator.queue.requeued",
            "{items}",
            "Total number of entities requeued (conflict, error-retry, or operator requeue).");
        _queueDiscarded = meter.CreateCounter<long>(
            "kubeops.operator.queue.discarded",
            "{items}",
            "Total number of reconciliation requests discarded due to a locking conflict.");
        _reconciliationTotal = meter.CreateCounter<long>(
            "kubeops.operator.reconciliation",
            "{reconciliations}",
            "Total number of reconciliations executed.");
        _reconciliationDuration = meter.CreateHistogram(
            "kubeops.operator.reconciliation.duration",
            "s",
            "Duration of a single reconciliation, including the entity fetch.",
            advice: new InstrumentAdvice<double>
            {
                // Reconciliations are typically sub-second to a few seconds. The OpenTelemetry default
                // buckets are sized for milliseconds, so override them with second-scale boundaries to
                // get usable latency percentiles.
                HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60],
            });
        _watcherEvents = meter.CreateCounter<long>(
            "kubeops.operator.watcher.events",
            "{events}",
            "Total number of Kubernetes watch events received.");
        _watcherReconnections = meter.CreateCounter<long>(
            "kubeops.operator.watcher.reconnections",
            "{reconnections}",
            "Total number of watcher reconnection attempts after an error.");
    }

    /// <summary>Records that an entity was enqueued.</summary>
    /// <param name="entityType">The watched entity type name.</param>
    /// <param name="triggerSource">The trigger source (<c>api_server</c> or <c>operator</c>).</param>
    public void RecordEnqueue(string entityType, string triggerSource)
        => _queueEnqueued.Add(
            1,
            new TagList { { EntityTypeTag, entityType }, { "kubeops.trigger.source", triggerSource } });

    /// <summary>Records that an entity was requeued.</summary>
    /// <param name="entityType">The watched entity type name.</param>
    /// <param name="reason">The requeue reason (<c>conflict</c>, <c>error_retry</c>, or <c>operator_requeue</c>).</param>
    public void RecordRequeue(string entityType, string reason)
        => _queueRequeued.Add(
            1,
            new TagList { { EntityTypeTag, entityType }, { "kubeops.requeue.reason", reason } });

    /// <summary>Records that a reconciliation request was discarded due to a locking conflict.</summary>
    /// <param name="entityType">The watched entity type name.</param>
    public void RecordDiscard(string entityType)
        => _queueDiscarded.Add(1, new TagList { { EntityTypeTag, entityType } });

    /// <summary>Records a completed reconciliation and its duration.</summary>
    /// <param name="entityType">The watched entity type name.</param>
    /// <param name="reconciliationType">The reconciliation type (<c>added</c>, <c>modified</c>, or <c>deleted</c>).</param>
    /// <param name="status">The outcome (<c>success</c> or <c>failure</c>).</param>
    /// <param name="durationSeconds">The reconciliation duration in seconds.</param>
    /// <param name="errorType">
    /// For failed reconciliations, a low-cardinality classification of the error following the
    /// OpenTelemetry <c>error.type</c> convention (typically the exception type's full name).
    /// Ignored for successful reconciliations.
    /// </param>
    public void RecordReconciliation(
        string entityType, string reconciliationType, string status, double durationSeconds, string? errorType = null)
    {
        var tags = new TagList
        {
            { EntityTypeTag, entityType },
            { "kubeops.reconciliation.type", reconciliationType },
            { "kubeops.reconciliation.status", status },
        };

        if (errorType is not null)
        {
            tags.Add("error.type", errorType);
        }

        _reconciliationTotal.Add(1, tags);
        _reconciliationDuration.Record(durationSeconds, tags);
    }

    /// <summary>Records a received watch event.</summary>
    /// <param name="entityType">The watched entity type name.</param>
    /// <param name="eventType">The watch event type (<c>added</c>, <c>modified</c>, <c>deleted</c>, or <c>bookmark</c>).</param>
    public void RecordWatchEvent(string entityType, string eventType)
        => _watcherEvents.Add(
            1,
            new TagList { { EntityTypeTag, entityType }, { "kubeops.watcher.event.type", eventType } });

    /// <summary>Records a watcher reconnection attempt.</summary>
    /// <param name="entityType">The watched entity type name.</param>
    public void RecordWatcherReconnection(string entityType)
        => _watcherReconnections.Add(1, new TagList { { EntityTypeTag, entityType } });

    /// <summary>
    /// Registers a depth provider for the given entity type. All providers are observed by a single
    /// shared <c>kubeops.operator.queue.depth</c> gauge that emits one measurement per entity type and
    /// <c>kubeops.queue.state</c> (<c>scheduled</c> = delayed, not yet ready; <c>ready</c> = ready to reconcile).
    /// </summary>
    /// <param name="entityType">The watched entity type name.</param>
    /// <param name="scheduledDepth">A callback returning the number of scheduled (delayed) entries.</param>
    /// <param name="readyDepth">A callback returning the number of ready entries.</param>
    public void RegisterQueueDepthGauge(string entityType, Func<int> scheduledDepth, Func<int> readyDepth)
        => _queueDepthProviders
            .GetOrAdd(entityType, _ => new ConcurrentQueue<QueueDepthProvider>())
            .Enqueue(new QueueDepthProvider(scheduledDepth, readyDepth));

    private IEnumerable<Measurement<int>> ObserveQueueDepth()
    {
        foreach (var (entityType, providers) in _queueDepthProviders)
        {
            var scheduled = 0;
            var ready = 0;
            var observed = false;

            foreach (var provider in providers)
            {
                try
                {
                    scheduled += provider.ScheduledDepth();
                    ready += provider.ReadyDepth();
                    observed = true;
                }
                catch (ObjectDisposedException)
                {
                    // The queue backing this provider was torn down (e.g. during shutdown). Skip it so a
                    // single disposed queue does not abort the observation for all other queues.
                }
            }

            if (!observed)
            {
                continue;
            }

            yield return new Measurement<int>(
                scheduled,
                new TagList { { EntityTypeTag, entityType }, { "kubeops.queue.state", "scheduled" } });
            yield return new Measurement<int>(
                ready,
                new TagList { { EntityTypeTag, entityType }, { "kubeops.queue.state", "ready" } });
        }
    }

    private sealed record QueueDepthProvider(Func<int> ScheduledDepth, Func<int> ReadyDepth);
}
