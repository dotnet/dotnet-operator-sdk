# KubeOps Abstractions

[![Nuget](https://img.shields.io/nuget/v/KubeOps.Abstractions?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Abstractions)
[![NuGet Pre-Release](https://img.shields.io/nuget/vpre/KubeOps.Abstractions?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Abstractions)

This package provides the fundamental building blocks for the KubeOps SDK. It defines the core interfaces, abstract base classes, and [.NET attributes](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/reflection-and-attributes/) used throughout the operator framework.

Think of this package as the contract definition for key KubeOps components.

## General Description

The `KubeOps.Abstractions` package is designed to provide a robust foundation for building Kubernetes operators using the KubeOps SDK. It offers a set of abstractions that allow developers to define custom resources, implement controllers, manage finalizers, and handle webhooks. By leveraging these abstractions, developers can create scalable and maintainable operator applications that interact seamlessly with Kubernetes.

## When to Use

Most projects building a KubeOps operator will reference the main `KubeOps.Operator` package, which includes this abstractions package as a dependency.

By depending only on this package, you can define your entities and interfaces without pulling in the full operator runtime or Kubernetes client logic, promoting better separation of concerns. This is primarily useful if you want to:

- Define your CRD entity classes in a separate library, shared between your operator and potentially other applications.
- Build tools that need to understand KubeOps entity definitions without needing the operator runtime.
