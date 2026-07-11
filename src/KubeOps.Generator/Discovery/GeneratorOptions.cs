// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KubeOps.Generator.Discovery;

internal static class GeneratorOptions
{
    private const string GeneratorNamespaceProperty = "build_property.KubeOpsGeneratorNamespace";

    public static IncrementalValueProvider<string?> GetGeneratorNamespace(
        IncrementalGeneratorInitializationContext context)
        => context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue(GeneratorNamespaceProperty, out var generatorNamespace)
            && !string.IsNullOrWhiteSpace(generatorNamespace)
                ? SanitizeNamespace(generatorNamespace)
                : null);

    private static string? SanitizeNamespace(string @namespace)
    {
        var segments = @namespace
            .Split('.')
            .Select(SanitizeSegment)
            .Where(segment => segment.Length > 0)
            .ToArray();

        return segments.Length == 0 ? null : string.Join(".", segments);
    }

    private static string SanitizeSegment(string segment)
    {
        var sanitized = new string(segment
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')
            .ToArray());

        if (sanitized.Length == 0)
        {
            return sanitized;
        }

        if (char.IsDigit(sanitized[0]) || SyntaxFacts.GetKeywordKind(sanitized) != SyntaxKind.None)
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }
}
