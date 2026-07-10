// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace KubeOps.Generator.Generators;

internal static class GeneratorSyntaxExtensions
{
    public static CompilationUnitSyntax AddNamespaceScope(this CompilationUnitSyntax unit, string? @namespace)
        => @namespace is null
            ? unit
            : unit.AddMembers(FileScopedNamespaceDeclaration(ParseName(@namespace)));

    public static string QualifyWith(this string className, string? @namespace)
        => @namespace is null ? className : $"{@namespace}.{className}";
}
