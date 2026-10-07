// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Operator.Metrics;

namespace KubeOps.Operator.Test.Metrics;

[Trait("Area", "Otel")]
public sealed class OperatorMetricsTest
{
    [Fact]
    public void RecordEnqueue_increments_counter_with_tags()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RecordEnqueue("V1Secret", "api_server");

        var measurement = harness.LongMeasurements.Should().ContainSingle().Subject;
        measurement.Instrument.Should().Be("kubeops.operator.queue.enqueued");
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().Contain("kubeops.entity.type", "V1Secret");
        measurement.Tags.Should().Contain("kubeops.trigger.source", "api_server");
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("error_retry")]
    [InlineData("operator_requeue")]
    public void RecordRequeue_increments_counter_with_reason(string reason)
    {
        using var harness = new MetricHarness();

        harness.Metrics.RecordRequeue("V1Secret", reason);

        var measurement = harness.LongMeasurements.Should().ContainSingle().Subject;
        measurement.Instrument.Should().Be("kubeops.operator.queue.requeued");
        measurement.Tags.Should().Contain("kubeops.requeue.reason", reason);
    }

    [Fact]
    public void RecordDiscard_increments_counter()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RecordDiscard("V1Secret");

        harness.LongMeasurements.Should().ContainSingle()
            .Which.Instrument.Should().Be("kubeops.operator.queue.discarded");
    }

    [Fact]
    public void RecordReconciliation_records_count_and_duration()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RecordReconciliation("V1Secret", "modified", "success", 1.5);

        harness.LongMeasurements.Should().ContainSingle()
            .Which.Instrument.Should().Be("kubeops.operator.reconciliation");

        var duration = harness.DoubleMeasurements.Should().ContainSingle().Subject;
        duration.Instrument.Should().Be("kubeops.operator.reconciliation.duration");
        duration.Value.Should().Be(1.5);
        duration.Tags.Should().Contain("kubeops.reconciliation.type", "modified");
        duration.Tags.Should().Contain("kubeops.reconciliation.status", "success");
    }

    [Fact]
    public void RecordReconciliation_failure_adds_error_type_tag()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RecordReconciliation("V1Secret", "added", "failure", 0.2, "System.TimeoutException");

        var measurement = harness.LongMeasurements.Should().ContainSingle().Subject;
        measurement.Tags.Should().Contain("kubeops.reconciliation.status", "failure");
        measurement.Tags.Should().Contain("error.type", "System.TimeoutException");
    }

    [Fact]
    public void RecordReconciliation_success_omits_error_type_tag()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RecordReconciliation("V1Secret", "added", "success", 0.2);

        harness.LongMeasurements.Should().ContainSingle()
            .Which.Tags.Should().NotContainKey("error.type");
    }

    [Fact]
    public void QueueDepthGauge_reports_multiple_entity_types_on_single_instrument()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RegisterQueueDepthGauge("V1Secret", () => 1, () => 2);
        harness.Metrics.RegisterQueueDepthGauge("V1ConfigMap", () => 3, () => 4);
        harness.Listener.RecordObservableInstruments();

        var depth = harness.IntMeasurements
            .Where(m => m.Instrument == "kubeops.operator.queue.depth")
            .ToList();

        // 2 entity types x 2 states, all on the single shared instrument.
        depth.Should().HaveCount(4);
        depth.Select(m => (string?)m.Tags["kubeops.entity.type"]).Distinct().Should()
            .BeEquivalentTo("V1Secret", "V1ConfigMap");
    }

    [Fact]
    public void QueueDepthGauge_skips_disposed_provider_without_breaking_others()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RegisterQueueDepthGauge(
            "V1Disposed",
            () => throw new ObjectDisposedException("queue"),
            () => throw new ObjectDisposedException("queue"));
        harness.Metrics.RegisterQueueDepthGauge("V1Healthy", () => 7, () => 8);
        harness.Listener.RecordObservableInstruments();

        var depth = harness.IntMeasurements
            .Where(m => m.Instrument == "kubeops.operator.queue.depth")
            .ToList();

        // Only the healthy provider's two measurements are reported; the disposed one is skipped.
        depth.Should().HaveCount(2);
        depth.Should().OnlyContain(m => (string?)m.Tags["kubeops.entity.type"] == "V1Healthy");
    }

    [Fact]
    public void RecordWatchEvent_and_reconnection_increment_counters()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RecordWatchEvent("V1Secret", "Added");
        harness.Metrics.RecordWatcherReconnection("V1Secret");

        harness.LongMeasurements.Select(m => m.Instrument).Should()
            .BeEquivalentTo("kubeops.operator.watcher.events", "kubeops.operator.watcher.reconnections");
    }

    [Theory]
    [InlineData("retry", "429")]
    [InlineData("failure", "403")]
    public void RecordWatcherResync_failure_adds_error_type_tag(string status, string errorType)
    {
        using var harness = new MetricHarness();

        harness.Metrics.RecordWatcherResync("V1Secret", status, errorType);

        var measurement = harness.LongMeasurements.Should().ContainSingle().Subject;
        measurement.Instrument.Should().Be("kubeops.operator.watcher.resyncs");
        measurement.Tags.Should().Contain("kubeops.entity.type", "V1Secret");
        measurement.Tags.Should().Contain("kubeops.resync.status", status);
        measurement.Tags.Should().Contain("error.type", errorType);
    }

    [Fact]
    public void RecordWatcherResync_success_omits_error_type_tag()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RecordWatcherResync("V1Secret", "success");

        var measurement = harness.LongMeasurements.Should().ContainSingle().Subject;
        measurement.Tags.Should().Contain("kubeops.resync.status", "success");
        measurement.Tags.Should().NotContainKey("error.type");
    }

    [Fact]
    public void QueueDepthGauge_reports_scheduled_and_ready()
    {
        using var harness = new MetricHarness();

        harness.Metrics.RegisterQueueDepthGauge("V1Secret", () => 3, () => 5);
        harness.Listener.RecordObservableInstruments();

        var depthMeasurements = harness.IntMeasurements
            .Where(m => m.Instrument == "kubeops.operator.queue.depth")
            .ToList();

        depthMeasurements.Should().HaveCount(2);
        depthMeasurements.Should().ContainSingle(m =>
            m.Value == 3 && (string?)m.Tags["kubeops.queue.state"] == "scheduled");
        depthMeasurements.Should().ContainSingle(m =>
            m.Value == 5 && (string?)m.Tags["kubeops.queue.state"] == "ready");
    }
}
