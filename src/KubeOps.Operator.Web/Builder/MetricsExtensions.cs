// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Builder;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Metrics;

namespace KubeOps.Operator.Web.Builder;

/// <summary>
/// Convenience extensions that wire up an OpenTelemetry Prometheus exporter for the operator's
/// metrics and expose the scraping endpoint via ASP.NET Core.
/// </summary>
public static class MetricsExtensions
{
    /// <summary>
    /// Registers an OpenTelemetry meter provider that exports the operator's metrics
    /// (meter named after <see cref="OperatorSettings.Name"/>) via the Prometheus exporter.
    /// Call <see cref="MapOperatorMetricsEndpoint"/> on the web application to expose the
    /// scraping endpoint.
    /// </summary>
    /// <param name="builder">The operator builder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>OperatorSettings.EnableMetrics</c> is disabled. Wiring up the exporter while the
    /// operator records no measurements is a misconfiguration, so this fails fast.
    /// </exception>
    public static IOperatorBuilder AddOperatorMetrics(this IOperatorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Settings.EnableMetrics)
        {
            throw new InvalidOperationException(
                "AddOperatorMetrics() requires metrics collection to be enabled, but " +
                "OperatorSettings.EnableMetrics is false. Enable it via WithMetrics() (the default) " +
                "or remove the AddOperatorMetrics() call.");
        }

        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(builder.Settings.Name)
                .AddPrometheusExporter());

        return builder;
    }

    /// <summary>
    /// Maps the Prometheus scraping endpoint that exposes the operator's metrics.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder (e.g. the <c>WebApplication</c>).</param>
    /// <param name="pattern">The endpoint pattern. Defaults to <c>/metrics</c>.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapOperatorMetricsEndpoint(
        this IEndpointRouteBuilder endpoints, string pattern = "/metrics")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPrometheusScrapingEndpoint(pattern);
        return endpoints;
    }
}
