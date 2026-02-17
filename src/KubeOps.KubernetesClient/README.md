# KubeOps Kubernetes Client

[![NuGet](https://img.shields.io/nuget/v/KubeOps.KubernetesClient?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.KubernetesClient)
[![NuGet Pre-Release](https://img.shields.io/nuget/vpre/KubeOps.KubernetesClient?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.KubernetesClient)

This package provides an enhanced, developer-friendly interface for interacting with the Kubernetes API, built on top of the official [kubernetes-client/csharp](https://github.com/kubernetes-client/csharp) library. While the official client is powerful, it often requires verbose calls, especially for Custom Resources.

The `KubeOps.KubernetesClient` simplifies common operator tasks by offering:

- **True Generic Methods:** Perform operations like `Get`, `List`, `Create`, `Update`, `Delete`, and `Watch` on any Kubernetes resource type (including custom resources defined with `[KubernetesEntity]`) using strongly-typed generic methods, without manually specifying API group, version, and plural name.
- **Simplified API:** Reduces boilerplate code for common CRUD operations.
- **Type Safety:** Leverages C# generics for better compile-time checking.

This is an enhanced version of the original [Google Kubernetes Client](https://github.com/kubernetes-client/csharp). It extends the original client with additional features, such as true generics and method variants. The original `GenericClient` supports generics in a limited way, requiring initialization with group and kind information.

The client acts as a wrapper, automatically handling the complexities of determining the correct API endpoint and resource mapping based on the provided C# type.

## Usage

### Dependency Injection (Recommended)

When using the main `KubeOps.Operator` package, an instance of `IKubernetesClient` is automatically registered in the .NET Dependency Injection container. You can inject it into your controllers, finalizers, or webhooks:

```csharp
using KubeOps.KubernetesClient;
using KubeOps.Operator.Controller;
using MyOperator.Entities; // Your custom entity
using k8s.Models; // For built-in types like V1Pod

public class MyResourceController : IResourceController<V1MyResource>
{
    private readonly IKubernetesClient _client;

    public MyResourceController(IKubernetesClient client)
    {
        _client = client;
    }

    public async Task<ResourceControllerResult?> ReconcileAsync(V1MyResource entity)
    {
        // Use the client to interact with the cluster
        var pod = await _client.GetAsync<V1Pod>("my-pod", entity.Namespace());
        if (pod == null)
        {
            var newPod = new V1Pod { /* ... */ };
            await _client.CreateAsync(newPod);
        }

        // Get a custom resource
        var otherResource = await _client.GetAsync<V1OtherResource>("other-resource-name", entity.Namespace());

        return null; // Requeue later
    }
}
```

### Standalone Usage

If you need to use the client outside the main KubeOps operator framework (e.g., in a command-line tool or script), you can instantiate it directly. The client automatically loads configuration based on standard Kubernetes conventions ([Kubeconfig file](https://kubernetes.io/docs/concepts/configuration/organize-cluster-access-kubeconfig/) or [in-cluster service account](https://kubernetes.io/docs/tasks/configure-pod-container/configure-service-account/)).

```csharp
using KubeOps.KubernetesClient;
using k8s.Models;

// Instantiate the client
IKubernetesClient client = new KubernetesClient();

// List all namespaces
var namespaces = await client.ListAsync<V1Namespace>();
foreach (var ns in namespaces)
{
    Console.WriteLine($"Namespace: {ns.Name()}");
}

// Get a specific ConfigMap
var configMap = await client.GetAsync<V1ConfigMap>("my-config", "default");
if (configMap != null)
{
    Console.WriteLine($"ConfigMap Data: {string.Join(',', configMap.Data)}");
}
```

For advanced configuration (e.g., custom Kubeconfig paths, timeouts), refer to the underlying `k8s.KubernetesClientConfiguration` documentation from the official client library.

## Examples

### List Resources

```csharp
var client = new KubernetesClient() as IKubernetesClient;

// Get all namespaces in the cluster
var namespaces = await client.ListAsync<V1Namespace>();

// List all pods in the 'staging' namespace
var podsInStaging = await client.ListAsync<V1Pod>("staging");

// List all custom resources of type V1MyCrd across all namespaces
// Note: This requires appropriate RBAC permissions for cluster-scoped resources
var allMyCrds = await client.ListAsync<V1MyCrd>(null);
```

### Get Resources

```csharp
// Get a Pod in the 'production' namespace
var pod = await client.GetAsync<V1Pod>("my-app-pod-xyz", "production");

// Get a custom resource
var myCrd = await client.GetAsync<V1MyCrd>("my-instance", "default");
```

### Create Resources

```csharp
var newConfigMap = new V1ConfigMap
{
    Metadata = new V1ObjectMeta { Name = "new-map", NamespaceProperty = "default" },
    Data = new Dictionary<string, string> { { "key", "value" } }
};
var createdMap = await client.CreateAsync(newConfigMap);
```

### Update Resources

```csharp
var existingPod = await client.GetAsync<V1Pod>("my-pod", "default");
if (existingPod != null)
{
    existingPod.Metadata.Annotations ??= new Dictionary<string, string>();
    existingPod.Metadata.Annotations["my-annotation"] = "updated-value";
    var updatedPod = await client.UpdateAsync(existingPod);
}
```

### Update Resource Status

```csharp
// Update the status of a custom resource
var existingCrd = await client.GetAsync<V1MyCrd>("my-instance", "default");
if (existingCrd != null)
{
    existingCrd.Status.Message = "Processing completed";
    var updatedCrd = await client.UpdateStatusAsync(existingCrd);
}
```

### Watch Resources

```csharp
// Watch for Pod events in the 'default' namespace
await foreach (var (type, pod) in client.WatchAsync<V1Pod>(namespaceParameter: "default"))
{
    Console.WriteLine($"Event: {type}, Pod: {pod.Name()}");
    // Handle Added, Modified, Deleted events
}
```

### Delete Resources

```csharp
// Delete by name and namespace
await client.DeleteAsync<V1Pod>("pod-to-delete", "default");

// Delete using an existing resource instance
var crdToDelete = await client.GetAsync<V1MyCrd>("crd-instance-to-delete", "dev");
if (crdToDelete != null)
{
    await client.DeleteAsync(crdToDelete);
}
```
### Server-Side Apply

Server-Side Apply (SSA) is the recommended approach for managing Kubernetes resources in operators and controllers. It provides declarative configuration with automatic three-way merging and field ownership tracking.

```csharp
// Apply a resource using Server-Side Apply
var myResource = new V1MyResource
{
    Metadata = new V1ObjectMeta 
    { 
        Name = "my-resource", 
        NamespaceProperty = "default" 
    },
    Spec = new V1MyResourceSpec 
    { 
        Replicas = 3,
        Image = "my-app:v1.0.0"
    }
};

// Apply with field manager tracking
// The fieldManager parameter identifies which controller owns which fields
var appliedResource = await client.ApplyAsync(
    myResource,
    fieldManager: "my-operator-controller");

// Apply with force to take ownership of conflicting fields
// Use with caution - this overrides other controllers' changes
var forcedApply = await client.ApplyAsync(
    myResource,
    fieldManager: "my-operator-controller",
    force: true);

// Dry-run to validate without making changes
var dryRunResult = await client.ApplyAsync(
    myResource,
    fieldManager: "my-operator-controller",
    dryRun: "All");
```

**Benefits of Server-Side Apply:**
- **Field Ownership Tracking:** Kubernetes tracks which field manager owns each field
- **Conflict Detection:** Automatically detects when multiple controllers try to manage the same fields
- **Three-Way Merge:** Intelligently merges your desired state with the current state
- **Declarative:** You specify the complete desired state; Kubernetes figures out what changed

**When to use ApplyAsync vs PatchAsync:**
- Use `ApplyAsync` for declarative resource management where you want to specify the complete desired state
- Use `PatchAsync` for tactical updates where you only want to modify specific fields
- `ApplyAsync` is recommended for operators and controllers managing resources long-term

For more details on Server-Side Apply, see the [Kubernetes documentation](https://kubernetes.io/docs/reference/using-api/server-side-apply/).

### Patch Resources

You can also patch resources using various patch strategies:

```csharp
// JSON Patch (RFC 6902) - tactical updates
var existingConfig = await client.GetAsync<V1ConfigMap>("my-config", "default");
if (existingConfig != null)
{
    existingConfig.Data["new-key"] = "new-value";
    var patchedConfig = await client.PatchAsync(existingConfig);
}

// Patch with field manager for Server-Side Apply compatibility
var patchedWithSSA = await client.PatchAsync(
    existingConfig,
    fieldManager: "my-operator",
    force: false);
```