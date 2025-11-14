# KubeOps Operator

[![NuGet](https://img.shields.io/nuget/v/KubeOps.Operator?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Operator)
[![NuGet Pre-Release](https://img.shields.io/nuget/vpre/KubeOps.Operator?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Operator)

The `KubeOps.Operator` package provides a framework for building Kubernetes operators in .NET. Built on top of the Kubernetes client libraries for .NET, it offers abstractions and utilities for implementing operators that manage custom resources in a Kubernetes cluster.

## Getting Started

Install the package from NuGet:

```bash
dotnet add package KubeOps.Operator
```

After installation, you can create entities, controllers, finalizers, and other components to implement your operator.

All resources must be registered with the operator builder to be recognized by the SDK and used as operator resources. The [`KubeOps.Generator`](https://dotnet.github.io/dotnet-operator-sdk/docs/packages/generator) provides convenience methods to register all components at once.

You'll need to use the Generic Host to run your operator. For a plain operator without webhooks, ASP.NET is not required (unlike v7).

```csharp
using KubeOps.Operator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Trace);

builder.Services
    .AddKubernetesOperator()
    .RegisterComponents();

using var host = builder.Build();
await host.RunAsync();
```

### Registering Resources

When using the KubeOps.Generator, you can use the `RegisterComponents` function:

```csharp
builder.Services
    .AddKubernetesOperator()
    .RegisterComponents();
```

Alternatively, you can register resources manually:

```csharp
builder.Services
    .AddKubernetesOperator()
    .AddController<TestController, V1TestEntity>()
    .AddFinalizer<FirstFinalizer, V1TestEntity>("first")
    .AddFinalizer<SecondFinalizer, V1TestEntity>("second");
```

### Entity

To create an entity, implement the `IKubernetesObject<V1ObjectMeta>` interface. The SDK provides convenience classes to help with initialization, status, and spec properties.

```csharp
[KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
public sealed class V1TestEntity :
    CustomKubernetesEntity<V1TestEntity.EntitySpec, V1TestEntity.EntityStatus>
{
    public override string ToString()
        => $"Test Entity ({Metadata.Name}): {Spec.Username} ({Spec.Email})";

    public class EntitySpec
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class EntityStatus
    {
        public string Status { get; set; } = string.Empty;
    }
}
```

### Controller

A controller reconciles a specific entity type. Implement controllers using the `IEntityController<TEntity>` interface. You can reconcile custom entities or other Kubernetes resources as long as they are registered with the operator. For guidance on reconciling external resources, refer to the [documentation](https://dotnet.github.io/dotnet-operator-sdk/).

Example controller implementation:

```csharp
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Logging;

[EntityRbac(typeof(V1TestEntity), Verbs = RbacVerb.All)]
public sealed class V1TestEntityController : IEntityController<V1TestEntity>
{
    private readonly IKubernetesClient _client;
    private readonly ILogger<V1TestEntityController> _logger;

    public V1TestEntityController(
        IKubernetesClient client,
        ILogger<V1TestEntityController> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ReconciliationResult<V1TestEntity>> ReconcileAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reconciling entity {Entity}.", entity);

        // Update status to indicate reconciliation in progress
        entity.Status.Status = "Reconciling";
        entity = await _client.UpdateStatus(entity);

        // Update status to indicate reconciliation complete
        entity.Status.Status = "Reconciled";
        await _client.UpdateStatus(entity);
        
        return ReconciliationResult<V1TestEntity>.Success(entity);
    }

    public Task<ReconciliationResult<V1TestEntity>> DeletedAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Entity {Entity} deleted.", entity);
        return Task.FromResult(ReconciliationResult<V1TestEntity>.Success(entity));
    }
}
```

This controller:

1. Updates the entity's status to indicate reconciliation is in progress
2. Updates the status again to indicate reconciliation is complete
3. Implements the required `DeletedAsync` method for handling deletion events

> **CAUTION:**
> Always use the returned values from modifying actions of the Kubernetes client. Failure to do so will result in "HTTP CONFLICT" errors due to the resource version field in the entity.

> **NOTE:**
> Do not update the entity itself in the reconcile loop. It is considered bad practice to update entities while reconciling them. However, the status may be updated. To update entities before they are reconciled (e.g., to validate or transform values), use webhooks instead.

### Finalizer

A [finalizer](https://kubernetes.io/docs/concepts/overview/working-with-objects/finalizers/) is a mechanism for asynchronous cleanup in Kubernetes. Implement finalizers using the `IEntityFinalizer<TEntity>` interface.

KubeOps provides automatic finalizer attachment and detachment to ensure proper resource cleanup. 
If you need special handling this automation can be disabled by configuration.
Finalizers then are attached using an `EntityFinalizerAttacher` and are called when the entity is marked for deletion.

```csharp
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Finalizer;

public sealed class FinalizerOne : IEntityFinalizer<V1TestEntity>
{
    public Task<ReconciliationResult<V1TestEntity>> FinalizeAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        // Implement cleanup logic here
        return Task.FromResult(ReconciliationResult<V1TestEntity>.Success(entity));
    }
}
```

> **NOTE:**
> The controller's `DeletedAsync` method will be called after all finalizers are removed.

## Documentation

For more information, visit the [documentation](https://dotnet.github.io/dotnet-operator-sdk/).