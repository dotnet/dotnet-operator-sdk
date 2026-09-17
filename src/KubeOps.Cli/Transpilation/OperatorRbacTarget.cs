// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Cli.Transpilation;

/// <summary>
/// The effective RBAC target of a generator run.
/// </summary>
/// <param name="Scope">The effective watch scope used to generate RBAC resources.</param>
/// <param name="Namespace">The namespace the operator resources are generated for.</param>
/// <param name="GenerateNamespace">Whether a namespace resource is part of the generated output.</param>
/// <param name="ReportDiagnostics">Whether the watch scope discovery diagnostics should be reported.</param>
internal sealed record OperatorRbacTarget(
    OperatorWatchScope Scope,
    string Namespace,
    bool GenerateNamespace,
    bool ReportDiagnostics);
