# KubeOps Generator

[![NuGet](https://img.shields.io/nuget/v/KubeOps.Generator?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Generator)
[![NuGet Pre-Release](https://img.shields.io/nuget/vpre/KubeOps.Generator?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Generator)

This is a C# source generator for KubeOps and operators.
It is used to generate convenience functions to help register
resources within an operator.

## Motivation

The primary goal of this generator is to reduce boilerplate code required in your operator's `Program.cs` (or startup logic). Instead of manually calling `builder.AddController<...>()` and `builder.AddFinalizer<...>()` for every component, the generator scans your project and creates extension methods that register all discovered components with a single call.

## Usage

The generator is automatically used when the `KubeOps.Generator` package is referenced.

```bash
dotnet add package KubeOps.Generator
```

which results in the following `csproj` reference:

```xml
<ItemGroup>
    <PackageReference Include="KubeOps.Generator" Version="...">
        <PrivateAssets>all</PrivateAssets>
        <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
</ItemGroup>
```

Once referenced, you typically use the main generated extension method in your `Program.cs`:

```csharp
using KubeOps.Operator;

var builder = WebApplication.CreateBuilder(args);

// Add KubeOps services and register all discovered components
builder.Services
    .AddKubernetesOperator()
    .RegisterComponents(); // <--- Generated extension method

// ... other service registrations

var app = builder.Build();
// ... configure middleware
app.RunOperatorAsync();
```

## Generated Sources

The generator will automatically generate functions for the `IOperatorBuilder`.

By default, all shared classes (`EntityDefinitions`, `EntityInitializer`,
`ControllerRegistrations`, `FinalizerRegistrations`, `OperatorBuilderExtensions`) are generated
into the **global namespace**. Two general rules apply:

- `ControllerRegistrations` / `FinalizerRegistrations` / `EntityDefinitions` /
  `EntityInitializer` are only generated when the compilation actually contains matching
  components.
- `OperatorBuilderExtensions` (the `RegisterComponents` aggregate) and `EntityInitializer` are
  `internal`: they are only meaningful for the compilation they are generated into.

### Configuring the Target Namespace

When multiple projects of one solution reference the generator (e.g. a class library with
controllers and the operator itself), the generated class names must not conflict. Set the
`KubeOpsGeneratorNamespace` MSBuild property in the participating projects to generate the shared
classes into a distinct namespace instead of the global namespace:

```xml
<PropertyGroup>
    <KubeOpsGeneratorNamespace>$(RootNamespace)</KubeOpsGeneratorNamespace>
</PropertyGroup>
```

Any namespace works (`$(RootNamespace)`, `$(AssemblyName)` or a literal); invalid characters are
sanitized. Note that top-level `Program.cs` files then need a using directive for the configured
namespace so that `RegisterComponents()` resolves.

### Multi-Assembly Composition

Controllers and finalizers may live in referenced class libraries that also use the generator.
Every assembly that contains such components is marked with an assembly-level
`KubeOps.Abstractions.Builder.KubeOpsGeneratedRegistrationsAttribute` pointing to its (public)
registration classes. These classes register **only the components of their own assembly** and
never chain into further assemblies.

The generated `RegisterComponents` method of the consuming compilation discovers these markers on
all transitively referenced assemblies and invokes every registration class exactly once via a
fully qualified call:

```csharp
using KubeOps.Abstractions.Builder;

[assembly: global::KubeOps.Abstractions.Builder.KubeOpsGeneratedRegistrations("MyOperator.ControllerRegistrations", "MyOperator.FinalizerRegistrations")]
namespace MyOperator;
internal static class OperatorBuilderExtensions
{
    public static IOperatorBuilder RegisterComponents(this IOperatorBuilder builder)
    {
        builder.RegisterControllers();
        builder.RegisterFinalizers();
        global::MyLibrary.ControllerRegistrations.RegisterControllers(builder);
        return builder;
    }
}
```

This flat, deduplicated composition guarantees that no component is registered twice, regardless
of how the assemblies reference each other (e.g. app → LibA → LibB with components in all three).

If two assemblies in the chain generate registration classes with the same fully qualified name
(e.g. both use the global namespace default), the generator reports the warning **KOG002**
and skips the conflicting registration instead of emitting ambiguous code. Set
`KubeOpsGeneratorNamespace` in the conflicting projects to resolve it.

### Entity Metadata / Entity Definitions

The generator creates a file named `EntityDefinitions.g.cs` (only when the compilation declares
entities). This file contains all entities that are annotated with the
`KubernetesEntityAttribute` and declared in the compilation itself - entities from referenced
assemblies are listed by their declaring assembly. The static class contains the
`EntityMetadata` for the entities.

#### Example

```csharp
using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;

public static class EntityDefinitions
{
    public static readonly EntityMetadata V1TestEntity = new("TestEntity", "v1", "testing.dev", null);
    public static IOperatorBuilder RegisterEntities(this IOperatorBuilder builder)
    {
        builder.AddEntity<global::Operator.Entities.V1TestEntity>(V1TestEntity);
        return builder;
    }
}
```

### Entity Initializer

All entities must have their `Kind` and `ApiVersion` fields set.
To achieve this, the generator creates an initializer file for each entity
that is annotated with the `KubernetesEntityAttribute`.

For each **partial** class that does not contain a default constructor,
the generator will create a default constructor that sets the `Kind` and `ApiVersion` fields.

For each **non-partial** class, a method extension is created that sets
the `Kind` and `ApiVersion` fields.

> **NOTE:**
> Setting your class as partial is crucial for the generator to create the constructor.
> Also, if a default constructor is already present, the generator uses the
> method extension fallback.

#### Example

```csharp
namespace Operator.Entities;

[KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
public partial class V1TestEntity : CustomKubernetesEntity
{
}
```

The **partial** defined entity above will generate the following `V1TestEntity.init.g.cs` file:

```csharp
namespace Operator.Entities;
public partial class V1TestEntity
{
    public V1TestEntity()
    {
        ApiVersion = "testing.dev/v1";
        Kind = "TestEntity";
    }
}
```

The **non-partial** defined entity below:

```csharp
namespace Operator.Entities;

[KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
public class V1TestEntity : CustomKubernetesEntity
{
}
```

will generate a static method extension in `EntityInitializer.g.cs`
for the entity to initialize the fields:

```csharp
public static class EntityInitializer
{
    public static global::Operator.Entities.V1ClusterTestEntity Initialize(this global::Operator.Entities.V1ClusterTestEntity entity)
    {
        entity.ApiVersion = "testing.dev/v1";
        entity.Kind = "ClusterTestEntity";
        return entity;
    }
}
```

### Controller Registrations

The generator creates a file named `ControllerRegistrations.g.cs` (only when the compilation
contains controllers). This file contains a function to register all found controllers
(i.e., classes that implement the `IResourceController<TEntity>` interface).

#### Example

```csharp
using KubeOps.Abstractions.Builder;

public static class ControllerRegistrations
{
    public static IOperatorBuilder RegisterControllers(this IOperatorBuilder builder)
    {
        builder.AddController<global::Operator.Controller.V1TestEntityController, global::Operator.Entities.V1TestEntity>();
        return builder;
    }
}
```

### Finalizer Registrations

The generator creates a file named `FinalizerRegistrations.g.cs` (only when the compilation
contains finalizers). This file contains all finalizers with generated finalizer identifiers.
Further, a function to register all finalizers (i.e., classes that implement `IResourceFinalizer<TEntity>`) is generated.

#### Example

```csharp
using KubeOps.Abstractions.Builder;

public static class FinalizerRegistrations
{
    public const string FinalizerOneIdentifier = "testing.dev/finalizeronefinalizer";
    public const string FinalizerTwoIdentifier = "testing.dev/finalizertwofinalizer";
    public static IOperatorBuilder RegisterFinalizers(this IOperatorBuilder builder)
    {
        builder.AddFinalizer<global::Operator.Finalizer.FinalizerOne, global::Operator.Entities.V1TestEntity>(FinalizerOneIdentifier);
        builder.AddFinalizer<global::Operator.Finalizer.FinalizerTwo, global::Operator.Entities.V1TestEntity>(FinalizerTwoIdentifier);
        return builder;
    }
}
```

### General Operator Extensions

The generator creates a file named `OperatorBuilder.g.cs`. It contains the internal
`RegisterComponents` convenience method that registers all generated sources: the components of
the own compilation (only the parts that exist) and the components of all marked referenced
assemblies (see [Multi-Assembly Composition](#multi-assembly-composition)).

#### Example

```csharp
using KubeOps.Abstractions.Builder;

[assembly: global::KubeOps.Abstractions.Builder.KubeOpsGeneratedRegistrations("ControllerRegistrations", "FinalizerRegistrations")]
internal static class OperatorBuilderExtensions
{
    public static IOperatorBuilder RegisterComponents(this IOperatorBuilder builder)
    {
        builder.RegisterControllers();
        builder.RegisterFinalizers();
        return builder;
    }
}
```

## Troubleshooting

- **Generated files not visible?** Source generators add files during the build process. They might not appear directly in your Visual Studio Solution Explorer unless you explicitly look in the `obj/Debug/netX.Y/generated/KubeOps.Generator` folder or use features like VS's "Show all files".
- **Changes not picked up?** If you add a new controller/finalizer/webhook and it's not being registered, ensure your project compiles successfully and try rebuilding the solution.
- **Entity Initializer not working?** Make sure your entity class is marked as `partial` and does not have an explicitly defined parameterless constructor if you want the generator to create one.
