// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Cli.Transpilation;

internal sealed record OperatorWatchScopeDiagnostic(
    string Message,
    string? FilePath = null,
    int? Line = null,
    int? Character = null)
{
    public override string ToString() => (FilePath, Line, Character) switch
    {
        ({ Length: > 0 } path, { } line, { } character) => $"{path}({line},{character}): {Message}",
        _ => Message,
    };
}
