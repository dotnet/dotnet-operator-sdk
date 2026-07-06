# KubeOps.Aspire.Hosting

[![NuGet](https://img.shields.io/nuget/v/KubeOps.Aspire.Hosting?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Aspire.Hosting)
[![NuGet Pre-Release](https://img.shields.io/nuget/vpre/KubeOps.Aspire.Hosting?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Aspire.Hosting)

`KubeOps.Aspire.Hosting` is the [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) **hosting** integration for KubeOps operators. It lets you orchestrate a KubeOps operator project as a resource inside a .NET Aspire AppHost.

It is the AppHost-side counterpart to the [`KubeOps.Aspire`](https://www.nuget.org/packages/KubeOps.Aspire) service-defaults integration.

## Usage

The repository sample uses a dedicated `examples/AspireOperator` project so the plain `examples/Operator` sample remains independent of Aspire.

In your Aspire AppHost project:

```csharp
var k8s = builder.AddKubernetesEnvironment("k8s")
    .WithHelm(helm =>
    {
        helm.WithChartName("my-operator");
        helm.WithReleaseName("my-operator");
        helm.WithNamespace("operator-system");
    });

var apiService = builder.AddProject<Projects.ApiService>("apiservice");

builder.AddKubeOps<Projects.AspireOperator>("operator")
    .RunWithKubernetes(k8s)
    .PublishAsKubernetesOperator(k8s)
    .WithReference(apiService);
```

`AddKubeOps<TProject>` behaves like a KubeOps-flavoured wrapper around the built-in `AddProject<TProject>` and returns the standard `IResourceBuilder<ProjectResource>`, so every Aspire extension (`WithReference`, `WithEnvironment`, `WaitFor`, ...) works as usual.

To complete the loop, add `AddKubeOpsServiceDefaults()` from the `KubeOps.Aspire` package in the operator project. This enables OpenTelemetry export to the Aspire dashboard and service discovery for the references wired up above.

## Run and publish behavior

`AddKubeOps<TProject>` does not start the operator during local Aspire runs unless you opt in with `RunWithKubernetes(...)`. This prevents an operator from accidentally using the developer's current kube context. With the default run options, missing CRDs are created before startup and only CRDs created by that AppHost run are removed on shutdown.

```csharp
var dev = builder.AddKubernetesEnvironment("dev");

builder.AddKubeOps<Projects.AspireOperator>("operator")
    .RunWithKubernetes(dev, run => run.WithPersistentCrds());
```

`RunWithKubernetes` also has a generic overload that accepts any `IResourceWithConnectionString` whose connection string is a kubeconfig path — for example a `K3sClusterResource` from the CommunityToolkit Aspire k3s integration. It injects the cluster's `KUBECONFIG` into the operator process, waits for the cluster, starts the operator automatically, and manages CRDs identically:

```csharp
var k3s = builder.AddK3sCluster("k3s");

builder.AddKubeOps<Projects.AspireOperator>("operator")
    .RunWithKubernetes(k3s);
```

When the AppHost uses Aspire's Kubernetes publishing support, `PublishAsKubernetesOperator(k8s)` invokes `kubeops generate operator` and appends the generated CRDs, RBAC resources, and service account to the Aspire-generated Helm chart. The operator deployment itself remains Aspire-owned, but KubeOps' generated deployment settings are merged into that workload so the chart deploys one correctly wired operator deployment.

```csharp
builder.AddKubeOps<Projects.AspireOperator>("operator")
    .PublishAsKubernetesOperator(k8s, publish =>
    {
        publish.Namespace = "operator-system";
        publish.WithServiceAccount("operator");
    });
```

If there is no Aspire Kubernetes environment, `PublishAsKubernetesOperator(...)` still participates in `aspire publish` by writing raw KubeOps YAML for the operator resource. This standalone path does not require Helm or a live Kubernetes cluster.

```csharp
builder.AddKubeOps<Projects.AspireOperator>("operator")
    .PublishAsKubernetesOperator(publish =>
    {
        publish.Namespace = "operator-system";
        publish.WithServiceAccount("operator");
    });
```

Use Azure and local targets independently when the development loop and deployment target are different:

```csharp
var dev = builder.AddKubernetesEnvironment("dev");
var aks = builder.AddAzureKubernetesEnvironment("aks");

builder.AddKubeOps<Projects.AspireOperator>("operator")
    .RunWithKubernetes(dev)
    .PublishAsKubernetesOperator(aks, publish => publish.WithServiceAccount("operator"));
```

See the [.NET Aspire guide](https://dotnet.github.io/dotnet-operator-sdk/docs/operator/aspire) for the full picture.
