# KubeOps CLI

[![NuGet](https://img.shields.io/nuget/v/KubeOps.Cli?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Cli)
[![NuGet Pre-Release](https://img.shields.io/nuget/vpre/KubeOps.Cli?label=NuGet&logo=nuget)](https://www.nuget.org/packages/KubeOps.Cli)

The KubeOps CLI is a command-line tool designed to simplify the development and management of Kubernetes Operators using .NET. It provides utilities for generating Custom Resource Definitions (CRDs) and managing operator-related tasks.

## Installation

### Global Installation

To install the KubeOps CLI globally on your machine:

```bash
dotnet tool install --global KubeOps.Cli
```

### Local Installation

To install the KubeOps CLI locally in your project:

```bash
dotnet new tool-manifest
```

```bash
dotnet tool install --local KubeOps.Cli
```

## Available Commands

### Generate

Generates Custom Resource Definitions (CRDs) and other Kubernetes-related resources for your operator.

```bash
kubeops generate
```

The `generate operator` command creates all necessary resources for deploying your operator to Kubernetes:

- **RBAC Rules**: Role-based access control configurations
- **Dockerfile**: Container image definition for your operator
- **Deployment**: Kubernetes deployment configuration
- **CRDs**: Custom Resource Definitions based on your C# entities
- **Namespace**: A dedicated namespace for your operator
- **Kustomization**: Kustomize configuration for managing all resources

If your operator includes webhooks (mutations or validations), additional resources are generated:

- **CA and Server Certificates**: For secure webhook communication
- **Webhook Configurations**: For validation and mutation webhooks
- **Service**: For exposing webhook endpoints
- **Secret Generators**: For managing webhook certificates

### Install

Installs the operator and its CRDs into a Kubernetes cluster.

```bash
kubeops install
```

### Uninstall

Removes the operator and its CRDs from a Kubernetes cluster.

```bash
kubeops uninstall
```

### Version

Displays the current version of the KubeOps CLI.

```bash
kubeops version
```

## Global Options

All commands support the following global options:

- `--no-ansi`: Disable ANSI colored output. This is automatically enabled when running in CI/CD environments (GitHub Actions, Azure Pipelines, Jenkins, GitLab CI, etc.)
- `--force` or `-f`: Skip confirmation prompts
- `--project`: Regex pattern to filter projects in a solution
- `--target-framework`: Specify target framework for solution projects

## Usage

The KubeOps CLI is designed to be used within .NET projects that implement Kubernetes operators. It helps streamline the development process by automating common tasks such as CRD generation and operator deployment.

For more detailed information about each command, use the `--help` flag:

```bash
kubeops [command] --help
```
