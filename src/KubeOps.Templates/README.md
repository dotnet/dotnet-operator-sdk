# KubeOps Dotnet New Templates

[![NuGet](https://img.shields.io/nuget/v/KubeOps.Templates?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Templates)
[![NuGet Pre-Release](https://img.shields.io/nuget/vpre/KubeOps.Templates?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Templates)

To use the operator SDK as easy as possible, this
[Nuget Package](https://www.nuget.org/packages/KubeOps.Templates)
contains `dotnet new` templates.
These templates enable developers to create Kubernetes operators
with the simple dotnet new command in C#.

## Installation

To install the template package, use the `dotnet` cli
(or you may use the exact version as provided in the link above):

```bash
dotnet new install KubeOps.Templates
```

As soon as the templates are installed, you may use them with the short names below:

## Templates

### operator

_Short Name_: `operator`

Creates a standard Kubernetes operator with demo implementations for controllers, entities, and finalizers. This template is a good starting point for most operator projects.

```bash
dotnet new operator -n MyOperator
```

### operator-empty

_Short Name_: `operator-empty`

Creates a minimal, empty Kubernetes operator project without web capabilities. Ideal for advanced users who want to start from scratch.

```bash
dotnet new operator-empty -n MyOperator
```

### operator-web

_Short Name_: `operator-web`

Creates a Kubernetes operator with web server capabilities and demo implementations, including webhooks. Use this template if you need web-based features like admission webhooks.

```bash
dotnet new operator-web -n MyOperator
```

### operator-web-empty

_Short Name_: `operator-web-empty`

Creates a minimal Kubernetes operator project with web server capabilities, but no demo implementations. Use this template if you want web features but a clean slate.

```bash
dotnet new operator-web-empty -n MyOperator
```
