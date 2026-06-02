// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Aspire;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace KubeOps.Aspire.Test;

public class KubeOpsServiceDefaultsTest
{
    [Fact]
    public void Should_Register_OpenTelemetry_Providers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddKubeOpsServiceDefaults("test-operator");

        using var provider = builder.Services.BuildServiceProvider();

        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void Should_Register_Self_Liveness_Health_Check()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddKubeOpsServiceDefaults("test-operator");

        using var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>();
        options.Value.Registrations.Should().Contain(r => r.Name == "self" && r.Tags.Contains("live"));
    }

    [Fact]
    public void Should_Register_Service_Discovery()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddKubeOpsServiceDefaults("test-operator");

        builder.Services.Should().Contain(descriptor =>
            descriptor.ServiceType.Namespace != null &&
            descriptor.ServiceType.Namespace.StartsWith("Microsoft.Extensions.ServiceDiscovery"));
    }
}
