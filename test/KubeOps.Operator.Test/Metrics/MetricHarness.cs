// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using KubeOps.Operator.Metrics;

namespace KubeOps.Operator.Test.Metrics;

/// <summary>
/// Captures the measurements of a fresh <see cref="OperatorMetrics"/> instance. The meter name is unique per
/// harness, so parallel tests do not see each other's measurements.
/// </summary>
internal sealed class MetricHarness : IDisposable
{
    private readonly TestMeterFactory _factory = new();

    public MetricHarness()
    {
        var meterName = $"test-operator-{Guid.NewGuid()}";
        Listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        Listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            LongMeasurements.Enqueue(new(instrument.Name, value, ToDictionary(tags))));
        Listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            DoubleMeasurements.Enqueue(new(instrument.Name, value, ToDictionary(tags))));
        Listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            IntMeasurements.Enqueue(new(instrument.Name, value, ToDictionary(tags))));

        Listener.Start();

        Metrics = new OperatorMetrics(_factory, meterName);
    }

    public OperatorMetrics Metrics { get; }

    public MeterListener Listener { get; }

    public ConcurrentQueue<CapturedMeasurement<long>> LongMeasurements { get; } = [];

    public ConcurrentQueue<CapturedMeasurement<double>> DoubleMeasurements { get; } = [];

    public ConcurrentQueue<CapturedMeasurement<int>> IntMeasurements { get; } = [];

    public void Dispose()
    {
        Listener.Dispose();
        _factory.Dispose();
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>(tags.Length);
        foreach (var tag in tags)
        {
            dictionary[tag.Key] = tag.Value;
        }

        return dictionary;
    }

    internal sealed record CapturedMeasurement<T>(string Instrument, T Value, IReadOnlyDictionary<string, object?> Tags);

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }

            _meters.Clear();
        }
    }
}
