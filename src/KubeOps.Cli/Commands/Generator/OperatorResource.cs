// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Cli.Commands.Generator;

internal enum OperatorResource
{
    /// <summary>Generate every resource.</summary>
    All,

    /// <summary>Generate role-based access control resources.</summary>
    Rbac,

    /// <summary>Generate the operator Dockerfile.</summary>
    Dockerfile,

    /// <summary>Generate webhook certificate files.</summary>
    Certificates,

    /// <summary>Generate the operator deployment and, where applicable, its service.</summary>
    Deployment,

    /// <summary>Generate admission webhook configurations.</summary>
    Webhooks,

    /// <summary>Generate custom resource definitions.</summary>
    Crds,

    /// <summary>Generate the operator namespace.</summary>
    Namespace,

    /// <summary>Generate the kustomization file.</summary>
    Kustomization,
}
