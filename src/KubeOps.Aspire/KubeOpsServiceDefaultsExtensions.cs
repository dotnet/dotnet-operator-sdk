// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Builder;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace KubeOps.Aspire;

/// <summary>
/// Adds .NET Aspire "service defaults" to a KubeOps operator: OpenTelemetry
/// (logging, metrics and tracing), OTLP export, service discovery, HTTP
/// resilience and default health checks.
/// </summary>
public static class KubeOpsServiceDefaultsExtensions
{
    /// <summary>
    /// Wires up the standard Aspire service defaults for a KubeOps operator.
    /// Call this once on the host builder, typically before
    /// <c>AddKubernetesOperator()</c>.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <param name="operatorName">
    /// Optional name used as the OpenTelemetry service and tracing
    /// <see cref="System.Diagnostics.ActivitySource"/> name. When <c>null</c>, the name is
    /// taken from the registered <see cref="OperatorSettings"/> (if
    /// <c>AddKubernetesOperator()</c> ran first) and otherwise falls back to
    /// <see cref="IHostEnvironment.ApplicationName"/>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TBuilder AddKubeOpsServiceDefaults<TBuilder>(this TBuilder builder, string? operatorName = null)
        where TBuilder : IHostApplicationBuilder
    {
        var serviceName = ResolveOperatorName(builder, operatorName);

        builder.ConfigureKubeOpsOpenTelemetry(serviceName);

        builder.Services.AddHealthChecks()

            // Liveness check: the operator process is up and the host has started.
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default, so calls to referenced Aspire services are retried.
            http.AddStandardResilienceHandler();

            // Resolve logical Aspire service names (e.g. "https+http://apiservice") via service discovery.
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry logging, metrics and tracing for the operator.
    /// When the <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variable (or
    /// configuration key) is present, the OTLP exporter is enabled for all signals.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <param name="serviceName">
    /// The OpenTelemetry service name and the tracing source name to subscribe to.
    /// This must match the operator name (<see cref="OperatorSettings.Name"/>), since
    /// KubeOps registers its <see cref="System.Diagnostics.ActivitySource"/> under that name.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TBuilder ConfigureKubeOpsOpenTelemetry<TBuilder>(this TBuilder builder, string serviceName)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics => metrics
                .AddRuntimeInstrumentation()
                .AddHttpClientInstrumentation())
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()

                // KubeOps registers an ActivitySource named after the operator (OperatorSettings.Name).
                .AddSource(serviceName));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    private static string ResolveOperatorName(IHostApplicationBuilder builder, string? operatorName)
    {
        if (!string.IsNullOrWhiteSpace(operatorName))
        {
            return operatorName;
        }

        var settings = builder.Services
            .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(OperatorSettings))?
            .ImplementationInstance as OperatorSettings;

        return settings?.Name
               ?? builder.Configuration["KubeOps:OperatorName"]
               ?? builder.Environment.ApplicationName;
    }
}
