// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Builder;
using KubeOps.Operator;
using KubeOps.Operator.Web.Builder;

using OpenTelemetry;

const string operatorName = "otel-operator";

var builder = WebApplication.CreateBuilder(args);

// Operator registration only contains operator building blocks.
builder.Services
    .AddKubernetesOperator(settings => settings.WithName(operatorName))
    .RegisterComponents();

// Observability is wired up separately through the standard OpenTelemetry pipeline.
// AddKubeOpsInstrumentation() subscribes to the operator's Meter and AddSource() to its
// ActivitySource (both named after OperatorSettings.Name). UseOtlpExporter() then exports
// every signal (metrics and traces) to an OTLP collector in a single, global call -
// configure the endpoint via the OTEL_EXPORTER_OTLP_ENDPOINT environment variable.
builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddKubeOpsInstrumentation())
    .WithTracing(tracing => tracing
        .AddSource(operatorName))
    .UseOtlpExporter();

var app = builder.Build();

app.UseRouting();

// Alternative to OTLP: expose a Prometheus scraping endpoint instead. Swap the metrics
// exporter for the Prometheus one and map the endpoint:
//
//     .WithMetrics(metrics => metrics
//         .AddKubeOpsInstrumentation()
//         .AddPrometheusExporter())
//
//     app.MapOperatorMetricsEndpoint(); // exposes GET /metrics

await app.RunAsync();
