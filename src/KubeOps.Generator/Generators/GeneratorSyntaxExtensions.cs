// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace KubeOps.Generator.Generators;

internal static class GeneratorSyntaxExtensions
{
    /// <summary>
    /// Adds a file scoped namespace declaration to the compilation unit when a namespace is given.
    /// Members added afterwards are rendered below the declaration and therefore belong to the
    /// namespace. Shared generated classes are scoped this way to the namespace configured via
    /// the <c>KubeOpsGeneratorNamespace</c> MSBuild property so that multiple assemblies using
    /// the generator do not declare conflicting types in the global namespace.
    /// </summary>
    public static CompilationUnitSyntax AddNamespaceScope(this CompilationUnitSyntax unit, string? @namespace)
        => @namespace is null
            ? unit
            : unit.AddMembers(FileScopedNamespaceDeclaration(ParseName(@namespace)));

    /// <summary>
    /// Prefixes a fully qualified (namespace-less) class name with the given namespace.
    /// </summary>
    public static string QualifyWith(this string className, string? @namespace)
        => @namespace is null ? className : $"{@namespace}.{className}";
}
