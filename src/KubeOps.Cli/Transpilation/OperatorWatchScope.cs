// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Cli.Transpilation;

internal sealed record OperatorWatchScope(
    OperatorWatchScopeKind Kind,
    string? Namespace = null,
    IReadOnlyList<OperatorWatchScopeDiagnostic>? Diagnostics = null)
{
    public static OperatorWatchScope ClusterWide { get; } = new(OperatorWatchScopeKind.ClusterWide);

    public static OperatorWatchScope Namespaced(string @namespace) =>
        new(OperatorWatchScopeKind.Namespaced, @namespace);

    public static OperatorWatchScope Unknown(params OperatorWatchScopeDiagnostic[] diagnostics) =>
        new(OperatorWatchScopeKind.Unknown, Diagnostics: diagnostics);
}
