# KubeOps - Kubernetes Operators in .NET

**Build Kubernetes Operators in .NET with Ease**

[![.NET Release](https://github.com/dotnet/dotnet-operator-sdk/actions/workflows/dotnet-release.yml/badge.svg?branch=main)](https://github.com/dotnet/dotnet-operator-sdk/actions/workflows/dotnet-release.yml)
[![.NET Pre-Release](https://github.com/dotnet/dotnet-operator-sdk/actions/workflows/dotnet-pre-release.yml/badge.svg)](https://github.com/dotnet/dotnet-operator-sdk/actions/workflows/dotnet-pre-release.yml)
[![Scheduled Code Security Testing](https://github.com/dotnet/dotnet-operator-sdk/actions/workflows/security-analysis.yml/badge.svg?event=schedule)](https://github.com/dotnet/dotnet-operator-sdk/actions/workflows/security-analysis.yml)

**KubeOps** is a [Kubernetes Operator](https://kubernetes.io/docs/concepts/extend-kubernetes/operator/) SDK designed for [.NET](https://dotnet.microsoft.com/) developers. It allows you to leverage your C# skills and the rich .NET ecosystem to build powerful Kubernetes controllers that automate the management of complex applications. KubeOps simplifies operator development by providing high-level abstractions, code generators, and helper utilities.

**For comprehensive documentation, tutorials, and API references, please visit the official [KubeOps Documentation Site](https://dotnet.github.io/dotnet-operator-sdk/).**

The documentation is also provided within the code itself (description of methods and classes), and each package contains a README with further information.

## Key Features

- **Define CRDs in C#:** Model your [Custom Resource Definitions (CRDs)](https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/) using plain C# classes and attributes.
- **Controller Logic:** Implement reconciliation logic using the `IEntityController<TEntity>` interface.
- **Finalizers:** Easily add cleanup logic before resource deletion with `IEntityFinalizer<TEntity>`.
- **Webhooks:** Create [Admission](https://kubernetes.io/docs/reference/access-authn-authz/extensible-admission-controllers/) (validating/mutating) and [Conversion](https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definition-versioning/#webhook-conversion) webhooks integrated with ASP.NET Core.
- **Code Generation:** Includes Roslyn source generators and a CLI tool (`kubeops`) to automate boilerplate code for CRDs, controllers, and RBAC rules.
- **Enhanced Kubernetes Client:** Provides convenience methods built on top of the official client library.
- **Leader Election:** Automatic handling for high-availability operator deployments.

## Getting Started

There are two ways to start building an operator with KubeOps:

1.  **Using the normal dotnet console template:**

    ```bash
    # Create a new console project
    dotnet new console -n MyOperator
    cd MyOperator

    # Add the KubeOps.Operator package
    dotnet add package KubeOps.Operator
    ```

2.  **Using the project templates:**

    ```bash
    # Install the templates
    dotnet new install KubeOps.Templates

    # Create a new operator project
    dotnet new operator -n MyOperator
    cd MyOperator
    ```

Both methods generate a basic operator structure with a sample custom resource, controller, and finalizer. The template approach is simpler and more direct, while the CLI provides additional commands for generating CRDs, RBAC rules, and more.

For detailed tutorials and guides, visit the [KubeOps Documentation Site](https://dotnet.github.io/dotnet-operator-sdk/).

## Packages

All packages target [.NET 8.0](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/overview) and [.NET 9.0](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/overview), leveraging modern C# features. The underlying Kubernetes client library (`KubernetesClient.Official`) also follows this versioning strategy.

The SDK is designed to be modular. You can include only the packages you need:

| Package                                                              | Description                                                                                                                                                                                                                                                                               |
| -------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [KubeOps.Abstractions](./src/KubeOps.Abstractions/README.md)         | Defines core interfaces, attributes (like `[KubernetesEntity]`), and base classes used across the SDK. Essential for defining your custom resources and controllers.                                                                                                                      |
| [KubeOps.Cli](./src/KubeOps.Cli/README.md)                           | A [.NET Tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools) providing commands for scaffolding projects, generating [Custom Resource Definitions (CRDs)](https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/), and more.  |
| [KubeOps.Generator](./src/KubeOps.Generator/README.md)               | Contains [Roslyn Source Generators](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) to automate boilerplate code generation for CRDs and controllers based on your definitions.                                                                     |
| [KubeOps.KubernetesClient](./src/KubeOps.KubernetesClient/README.md) | Provides an enhanced client for interacting with the [Kubernetes API](https://kubernetes.io/docs/reference/kubernetes-api/), built on top of the official `KubernetesClient` library. Offers convenience methods for common operator tasks.                                               |
| [KubeOps.Operator](./src/KubeOps.Operator/README.md)                 | The main engine of the SDK. Handles reconciling resources, watching for changes, and managing the operator lifecycle. This is the primary package needed to run an operator.                                                                                                              |
| [KubeOps.Operator.Web](./src/KubeOps.Operator.Web/README.md)         | Integrates the operator with ASP.NET Core to expose endpoints for features like [Admission Webhooks](https://kubernetes.io/docs/reference/access-authn-authz/extensible-admission-controllers/).                                                                                          |
| [KubeOps.Transpiler](./src/KubeOps.Transpiler/README.md)             | Utilities for converting .NET type definitions into Kubernetes YAML manifests, specifically for [CRDs](https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/) and [RBAC](https://kubernetes.io/docs/reference/access-authn-authz/rbac/) rules. |
| [KubeOps.Templates](./src/KubeOps.Templates/README.md)               | Project templates for creating new operator projects. Provides a quick start with pre-configured project structure and sample resources.                                                                                                                                                  |

## Examples

You can find various example operators demonstrating different features in the [`examples/`](https://github.com/dotnet/dotnet-operator-sdk/tree/main/examples/) directory of this repository.

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details. This license applies to all packages in the KubeOps SDK.

## .NET Foundation

This project is supported by the [.NET Foundation](https://dotnetfoundation.org).

## Governance

KubeOps is maintained by the repository collaborators and maintainers. We welcome community contributions and aim to foster an open and collaborative development process. Decision-making primarily occurs through GitHub issues and pull requests.

## Contribution

If you want to contribute, feel free to open a pull request or write issues :-)
Please note that this project is released with a [Contributor Code of Conduct](./CODE_OF_CONDUCT.md).
By participating in this project you agree to abide by its terms.

Read more about contribution (especially for setting up your local environment)
in the [CONTRIBUTING file](./CONTRIBUTING.md).

In short:

- Check out the code
- Develop on KubeOps
- Use some Kubernetes to run the test operator against
- Create tests
- Build the whole solution (lint warnings will result in an error)
- Open PR

## Motivation

KubeOps aims to provide a first-class experience for developing Kubernetes operators within the .NET ecosystem, offering an alternative to Go-based SDKs like Kubebuilder and Operator SDK, while embracing familiar C# patterns and tooling.
