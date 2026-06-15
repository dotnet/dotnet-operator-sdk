// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.Metrics;

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Operator.Web.Builder;

using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KubeOps.Operator.Web.Test.Metrics;

[Trait("Area", "Otel")]
public sealed class MetricsExtensionsTest
{
    private const string OperatorName = "test-operator";

    [Fact]
    public void Should_Subscribe_Meter_Resolved_From_Settings()
    {
        var exported = new List<Metric>();
        var services = new ServiceCollection();
        services.AddSingleton(new OperatorSettingsBuilder { Name = OperatorName }.Build());
        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddKubeOpsInstrumentation()
                .AddInMemoryExporter(exported));

        using var provider = services.BuildServiceProvider();
        var meterProvider = provider.GetRequiredService<MeterProvider>();

        using var meter = new Meter(OperatorName);
        meter.CreateCounter<long>("test.counter").Add(1);

        meterProvider.ForceFlush();

        exported.Should().Contain(m => m.Name == "test.counter");
    }

    [Fact]
    public void Should_Subscribe_Meter_By_Explicit_Name()
    {
        var exported = new List<Metric>();
        var services = new ServiceCollection();
        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddKubeOpsInstrumentation(OperatorName)
                .AddInMemoryExporter(exported));

        using var provider = services.BuildServiceProvider();
        var meterProvider = provider.GetRequiredService<MeterProvider>();

        using var meter = new Meter(OperatorName);
        meter.CreateCounter<long>("test.counter").Add(1);

        meterProvider.ForceFlush();

        exported.Should().Contain(m => m.Name == "test.counter");
    }
}
