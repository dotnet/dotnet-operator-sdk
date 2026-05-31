# KubeOps.Aspire.Hosting

`KubeOps.Aspire.Hosting` is the [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) **hosting** integration for KubeOps operators. It lets you orchestrate a KubeOps operator project as a resource inside a .NET Aspire AppHost.

It is the AppHost-side counterpart to the [`KubeOps.Aspire`](https://www.nuget.org/packages/KubeOps.Aspire) service-defaults integration.

## Usage

In your Aspire AppHost project:

```csharp
var apiService = builder.AddProject<Projects.ApiService>("apiservice");

builder.AddKubeOps<Projects.Operator>("operator")
    .WithReference(apiService);
```

`AddKubeOps<TProject>` is a thin, KubeOps-flavoured wrapper around the built-in `AddProject<TProject>` and returns the standard `IResourceBuilder<ProjectResource>`, so every Aspire extension (`WithReference`, `WithEnvironment`, `WaitFor`, ...) works as usual.

To complete the loop, add `AddKubeOpsServiceDefaults()` from the `KubeOps.Aspire` package in the operator project. This enables OpenTelemetry export to the Aspire dashboard and service discovery for the references wired up above.

See the [.NET Aspire guide](https://dotnet.github.io/dotnet-operator-sdk/docs/operator/aspire) for the full picture.
