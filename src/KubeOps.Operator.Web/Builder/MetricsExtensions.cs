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
/// Convenience extensions that subscribe an OpenTelemetry meter provider to the operator's metrics
/// and expose the Prometheus scraping endpoint via ASP.NET Core. The operator records its metrics on
/// a <see cref="System.Diagnostics.Metrics.Meter"/> named after <see cref="OperatorSettings.Name"/>.
/// </summary>
public static class MetricsExtensions
{
    /// <summary>
    /// Subscribes the meter provider to the operator's metrics. The operator name is resolved from
    /// the registered <see cref="OperatorSettings"/>, so <c>AddKubernetesOperator()</c> must have run
    /// on the same service collection. Use <see cref="AddKubeOpsInstrumentation(MeterProviderBuilder, string)"/>
    /// to pass the name explicitly.
    /// </summary>
    /// <param name="builder">The meter provider builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static MeterProviderBuilder AddKubeOpsInstrumentation(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder is IDeferredMeterProviderBuilder deferred)
        {
            return deferred.Configure((sp, b) =>
                b.AddMeter(sp.GetRequiredService<OperatorSettings>().Name));
        }

        return builder;
    }

    /// <summary>
    /// Subscribes the meter provider to the operator's metrics using an explicit operator name.
    /// </summary>
    /// <param name="builder">The meter provider builder.</param>
    /// <param name="operatorName">The operator name (must equal <see cref="OperatorSettings.Name"/>).</param>
    /// <returns>The builder for chaining.</returns>
    public static MeterProviderBuilder AddKubeOpsInstrumentation(this MeterProviderBuilder builder, string operatorName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorName);

        return builder.AddMeter(operatorName);
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
