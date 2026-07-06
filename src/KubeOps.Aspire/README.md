# KubeOps.Aspire

[![NuGet](https://img.shields.io/nuget/v/KubeOps.Aspire?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Aspire)
[![NuGet Pre-Release](https://img.shields.io/nuget/vpre/KubeOps.Aspire?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Aspire)

`KubeOps.Aspire` is the [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) **service defaults** integration for KubeOps operators. It is the operator-side counterpart to the [`KubeOps.Aspire.Hosting`](https://www.nuget.org/packages/KubeOps.Aspire.Hosting) AppHost integration.

A single call wires up the cross-cutting concerns that Aspire expects from a well-behaved resource:

- **OpenTelemetry** &mdash; logging, metrics and tracing, including the operator's `ActivitySource`.
- **OTLP export** &mdash; enabled automatically when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (Aspire injects this).
- **Service discovery** &mdash; so the operator can call other Aspire resources by their logical name.
- **HTTP resilience** &mdash; a standard resilience handler on all `HttpClient` instances.
- **Health checks** &mdash; a default `self` liveness check.

## Usage

```csharp
using KubeOps.Aspire;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddKubernetesOperator()
    .RegisterComponents();

builder.AddKubeOpsServiceDefaults();

using var host = builder.Build();
await host.RunAsync();
```

Call `AddKubeOpsServiceDefaults()` **after** `AddKubernetesOperator()` so the OpenTelemetry service and tracing source names match `OperatorSettings.Name` and the operator's reconciliation traces are captured. If you must call it earlier, pass the name explicitly (and keep it in sync with `OperatorSettings.Name`):

```csharp
builder.AddKubeOpsServiceDefaults("my-operator");
```

See the [.NET Aspire guide](https://dotnet.github.io/dotnet-operator-sdk/docs/operator/aspire) for the full picture.
