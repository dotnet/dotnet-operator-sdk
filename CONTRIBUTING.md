# Contributing to KubeOps

First of all, thank you for considering contributing to KubeOps.
This is an open souce project and shall be driven by the community.

This project and everyone participating in it is governed by the
[.NET Foundation Code of Conduct](https://dotnetfoundation.org/about/policies/code-of-conduct). By participating, you are
expected to uphold this code.

This document describes how contributions may be done and what is required
to develop on KubeOps.

## Creating/Reporting Issues

Feel free to open an issue in the [issues section](https://github.com/dotnet/dotnet-operator-sdk/issues).
There are three issue templates:

- Bug: to report an issue/bug that prevents usage or is an inconvenience of KubeOps
- Feature request: to report a new feature that would enhance KubeOps
- Documentation: to report missing / wrong documentation

Please search through the already created issues to find similarities.

## Creating Pull Requests

To directly contribute to the solution, create a fork of the repository
and implement your addition. Please keep in mind that reviewing takes some
time and is not done instantly. The people working on the project are volunteers
and will do their best to review your PR as soon as possible.

Please adhere to the linting rules and the general code style in the repository.
Also, add tests for your changes to ensure that the system works well
when other changes happen.

The PR name should adhere to
the repositories standard naming. Please name your PR
with [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/#summary).

**NOTE for breaking changes**: please state breaking changes
in the PR description. The review process will be faster when
breaking changes are well documented.

A few examples:

- "fix: Null exception during watcher process"
- "feat(core): Add new functionality"
- "feat(testing): expose kubernetes client for testing"
- "refactor: changed this and that"
- "docs: Add docs about KubeOps core"

The PR will be squashed and merged into the default branch.

## Local Development

To set up a local development environment, you'll need to perform the following steps:

- Check out the repository (or your fork)
- If you want to run the Operator locally, you'll need some Kubernetes instance.
  This can be any Kubernetes instance you'd like:
  - Local Kubernetes in Docker for Mac/Windows
  - minikube / any other local Kubernetes
  - Deployed Kubernetes (e.g. GCP Kubernetes instance)
- You can now code your stuff.
- Use the implementation in the examples folder to test your changes. Those examples
  may also be extended or new ones added for testing purposes.
- Write tests for your changes!
- Build the whole solution and check for linting errors / warnings.
  **NOTE** that any warning will result in an error when building
  with `Release` configuration.
- Do not change the linting rules without creating a discussion/issue first.
- Create a PR with an appropriate name and description.
