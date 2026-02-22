# KubeOps Web Operator

[![NuGet](https://img.shields.io/nuget/v/KubeOps.Operator.Web?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Operator.Web)
[![NuGet Pre-Release](https://img.shields.io/nuget/vpre/KubeOps.Operator.Web?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Operator.Web)

**This package requires your operator to run as an ASP.NET Core web application.**

This package integrates KubeOps with ASP.NET Core, enabling your operator to host HTTP endpoints required for Kubernetes [Admission Webhooks](https://kubernetes.io/docs/reference/access-authn-authz/extensible-admission-controllers/) and [CRD Conversion Webhooks](https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definition-versioning/#configure-a-conversion-webhook).

When the Kubernetes API server needs to validate, mutate, or convert a resource as configured in a `ValidatingWebhookConfiguration`, `MutatingWebhookConfiguration`, or `CustomResourceDefinition`, it sends an HTTP request to a service endpoint. This package provides the necessary infrastructure to receive and handle these requests within your .NET operator.

> For a comprehensive explanation of different webhook types, how to implement their logic using KubeOps base classes/interfaces, and how to configure Kubernetes resources to use them, please refer to the main **[Webhooks Documentation](https://dotnet.github.io/dotnet-operator-sdk/docs/operator/building-blocks/webhooks)**.

## Setup

Using this package requires structuring your operator as an ASP.NET Core web application:

1. **Project SDK:** Ensure your project file (`.csproj`) uses the `Microsoft.NET.Sdk.Web` SDK:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk.Web">
     ...
   </Project>
   ```

2. **Program.cs Configuration:** Modify your `Program.cs` to configure both KubeOps and ASP.NET Core routing:

   ```csharp
   using KubeOps.Operator;

   var builder = WebApplication.CreateBuilder(args);

   // 1. Add KubeOps services and register components (controllers, finalizers, webhooks)
   builder.Services
       .AddKubernetesOperator()
       .RegisterComponents(); // Scans assemblies for controllers, finalizers, webhooks

   // Alternatively, register webhooks explicitly:
   // builder.Services.AddWebhook<MyValidationWebhook>();
   // builder.Services.AddWebhook<MyMutationWebhook>();
   // builder.Services.AddWebhook<MyConversionWebhook>();

   // 2. Add standard ASP.NET Core MVC services
   builder.Services.AddControllers();

   var app = builder.Build();

   // 3. Enable ASP.NET Core routing
   app.UseRouting();

   // 4. Map controllers (including webhook endpoints)
   app.MapControllers();

   // 5. Run the operator and web host
   await app.RunAsync();
   ```

Key points:

- `AddKubernetesOperator()`: Initializes core KubeOps services.
- `RegisterComponents()`: **(Recommended)** Automatically discovers and registers implementations of controllers, finalizers, and webhooks based on attributes and base classes found within the specified or entry assemblies. You can also use `AddWebhook<T>()` for explicit registration if needed.
- `AddControllers()`: Adds services required for MVC controllers, which KubeOps webhooks are built upon.
- `MapControllers()`: Configures the ASP.NET Core routing middleware to direct incoming HTTP requests to the correct webhook controller actions.

## Webhook Endpoint Routing

KubeOps automatically configures routes for your webhooks based on the attributes you apply:

- `[ValidationWebhook(typeof(TEntity))]`: Creates an endpoint at `/validate/{entity-type-name}`
- `[MutationWebhook(typeof(TEntity))]`: Creates an endpoint at `/mutate/{entity-type-name}`
- `[ConversionWebhook(typeof(TStorageEntity))]`: Creates an endpoint at `/convert/{group}/{plural-name}`

The `{entity-type-name}` is derived from the lowercase C# type name of the entity passed to the webhook attribute (e.g., `V1TestEntity` maps to `/validate/v1testentity`).
For conversion webhooks, the URL is constructed from the entity's group and plural name from its metadata (e.g., `/convert/webhook.dev/testentities`).

## Implementation Overview

(Refer to the main [Webhooks Documentation](https://dotnet.github.io/dotnet-operator-sdk/docs/operator/building-blocks/webhooks) for full details and examples)

- **Validation Hooks:** Inherit from `ValidationWebhook<TEntity>` and decorate with `[ValidationWebhook(typeof(TEntity))]`
- **Mutation Hooks:** Inherit from `MutationWebhook<TEntity>` and decorate with `[MutationWebhook(typeof(TEntity))]`
- **Conversion Hooks:** Inherit from `ConversionWebhook<TStorageEntity>` and decorate with `[ConversionWebhook(typeof(TStorageEntity))]`. Implement `IEntityConverter<,>` for conversions.

## Important Considerations

- **TLS:** Kubernetes requires webhook endpoints to be served over HTTPS. Managing TLS certificates and configuring the API server to trust them is crucial.
- **Dependencies:** This package brings in ASP.NET Core dependencies.

## Validation Hooks

To create a validation webhook, create a new class that inherits from `ValidationWebhook<T>` and decorate it with the `ValidationWebhookAttribute` to set the route correctly.

You can override any of the following methods:

- `Create`
- `CreateAsync`
- `Update`
- `UpdateAsync`
- `Delete`
- `DeleteAsync`

The async methods take precedence over the sync methods.

Example validation webhook:

```csharp
[ValidationWebhook(typeof(V1TestEntity))]
public class TestValidationWebhook(ILogger<TestValidationWebhook> logger) : ValidationWebhook<V1TestEntity>
{
    public override ValidationResult Create(V1TestEntity entity, bool dryRun)
    {
        if (entity.Spec.Username == "forbidden")
        {
            return Fail("name may not be 'forbidden'.", 422);
        }

        return Success();
    }

    public override ValidationResult Update(V1TestEntity oldEntity, V1TestEntity newEntity, bool dryRun)
    {
        if (newEntity.Spec.Username == "forbidden")
        {
            return Fail("name may not be 'forbidden'.");
        }

        return Success();
    }
}
```

Use the `protected` methods (`Success` and `Fail`) to create validation results, similar to "normal" `IActionResult` creation methods.

## Mutation Hooks

To create a mutation webhook, create a new class that inherits from `MutationWebhook<T>` and decorate it with the `MutationWebhookAttribute` to set the route correctly.

You can override any of the following methods:

- `Create`
- `CreateAsync`
- `Update`
- `UpdateAsync`
- `Delete`
- `DeleteAsync`

The async methods take precedence over the sync methods.

Example mutation webhook:

```csharp
[MutationWebhook(typeof(V1TestEntity))]
public class TestMutationWebhook(ILogger<TestMutationWebhook> logger) : MutationWebhook<V1TestEntity>
{
    public override MutationResult<V1TestEntity> Create(V1TestEntity entity, bool dryRun)
    {
        if (entity.Spec.Username == "overwrite")
        {
            entity.Spec.Username = "random overwritten";
            return Modified(entity);
        }

        return NoChanges();
    }
}
```

Use the `protected` methods (`NoChanges`, `Modified`, and `Fail`) to create mutation results, similar to "normal" `IActionResult` creation methods.

## Conversion Hooks

> **CAUTION:**
> Conversion webhooks are not stable yet. The API may change in the future without a new major version. All code related to conversion webhooks is attributed with the `RequiresPreviewFeatures` attribute. To use these features, you need to enable preview features in your project file with the `<EnablePreviewFeatures>true</EnablePreviewFeatures>` property.

A conversion webhook allows you to convert Kubernetes resources between versions. The webhooks are installed in CRDs and are called for all objects that need conversion (i.e., to achieve the stored version state).

A conversion webhook consists of two parts:

1. The webhook itself (the MVC controller that registers its route within ASP.NET)
2. The conversion logic

The following example demonstrates two versions of the "TestEntity" (v1 and v2) and implements a conversion webhook to convert between them:

```csharp
// The Kubernetes resources
[KubernetesEntity(Group = "webhook.dev", ApiVersion = "v1", Kind = "TestEntity")]
public partial class V1TestEntity : CustomKubernetesEntity<V1TestEntity.EntitySpec>
{
    public override string ToString() => $"Test Entity v1 ({Metadata.Name}): {Spec.Name}";

    public class EntitySpec
    {
        public string Name { get; set; } = string.Empty;
    }
}

[KubernetesEntity(Group = "webhook.dev", ApiVersion = "v2", Kind = "TestEntity")]
public partial class V2TestEntity : CustomKubernetesEntity<V2TestEntity.EntitySpec>
{
    public override string ToString() => $"Test Entity v2 ({Metadata.Name}): {Spec.Firstname} {Spec.Lastname}";

    public class EntitySpec
    {
        public string Firstname { get; set; } = string.Empty;
        public string Lastname { get; set; } = string.Empty;
    }
}
```

The v1 resource has first and last name in a single field, while v2 separates them:

```csharp
public class V1ToV2 : IEntityConverter<V1TestEntity, V2TestEntity>
{
    public V2TestEntity Convert(V1TestEntity from)
    {
        var nameSplit = from.Spec.Name.Split(' ');
        var result = new V2TestEntity { Metadata = from.Metadata };
        result.Spec.Firstname = nameSplit[0];
        result.Spec.Lastname = string.Join(' ', nameSplit[1..]);
        return result;
    }

    public V1TestEntity Revert(V2TestEntity to)
    {
        var result = new V1TestEntity { Metadata = to.Metadata };
        result.Spec.Name = $"{to.Spec.Firstname} {to.Spec.Lastname}";
        return result;
    }
}
```

The conversion logic is implemented in the `IEntityConverter` interface. Each converter has a "convert" (from -> to) and a "revert" (to -> from) method.

```csharp
[ConversionWebhook(typeof(V2TestEntity))] // Attribute defines the route
public class TestConversionWebhook : ConversionWebhook<V2TestEntity> // Provides ASP.NET Core Controller behavior
{
    protected override IEnumerable<IEntityConverter<V2TestEntity>> Converters =>
    [
        new V1ToV2(), // other versions...
    ];
}
```

The webhook registers the list of possible converters and calls the appropriate converter upon request.

> **NOTE:**
> There needs to be a conversion between ALL versions to the stored version (newest version). If there is no conversion, the webhook will fail and the resource is not stored. So if there exist v1, v2, and v3, there needs to be a converter for v1 -> v3 and v2 -> v3 (when v3 is the stored version).

## Installing In The Cluster

When creating an operator with webhooks, certain special resources must be provided to run in the cluster. When this package is referenced and KubeOps.Cli is installed, these resources should be generated automatically. Instead of generating a Dockerfile with `dotnet:runtime` as the final image, you'll need `dotnet:aspnet`, and the operator needs a service and certificates for the HTTPS connection since webhooks only operate over HTTPS.

With the KubeOps.Cli package, you can generate the required resources or let the customized Build targets do it for you. The targets create:

- A CA certificate and a server certificate (with respective keys)
- A service
- The webhook registrations required for your operator

> **WARNING:**
> The generated certificate has a validity of 5 years. After that time, the certificate needs to be renewed. For now, there is no automatic renewal process.

## Webhook Development

The Operator Web package can be configured to generate self-signed certificates on startup and create/update your webhooks in the Kubernetes cluster to point to your development machine. To use this feature, use the `CertificateGenerator` class and `UseCertificateProvider()` operator builder extension method:

```csharp
var builder = WebApplication.CreateBuilder(args);
string ip = "192.168.1.100";
ushort port = 443;

using CertificateGenerator generator = new(ip);
using X509Certificate2 cert = generator.Server.CopyServerCertWithPrivateKey();

// Configure Kestrel to listen on IPv4, use port 443, and use the server certificate
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(System.Net.IPAddress.Any, port, listenOptions =>
    {
        listenOptions.UseHttps(cert);
    });
});

builder.Services
    .AddKubernetesOperator()
    // Create the development webhook service using the cert provider
    .UseCertificateProvider(port, ip, generator)
    // More code for generation, controllers, etc.
```

The `UseCertificateProvider` method takes an `ICertificateProvider` interface, so it can be used to implement your own certificate generator/loader for development if necessary.