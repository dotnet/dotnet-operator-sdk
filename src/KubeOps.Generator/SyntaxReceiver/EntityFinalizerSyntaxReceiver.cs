// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KubeOps.Generator.SyntaxReceiver;

internal sealed class EntityFinalizerSyntaxReceiver : ISyntaxContextReceiver
{
    private const string IEntityFinalizerMetadataName = "KubeOps.Abstractions.Reconciliation.Finalizer.IEntityFinalizer`1";

#pragma warning disable RS1024
    private readonly HashSet<INamedTypeSymbol> _visitedTypeSymbols = new(SymbolEqualityComparer.Default);
#pragma warning restore RS1024

    public List<(ClassDeclarationSyntax Finalizer, string EntityName)> Finalizer { get; } = [];

    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclarationSyntax)
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax) is not INamedTypeSymbol classSymbol)
        {
            return;
        }

        if (!_visitedTypeSymbols.Add(classSymbol))
        {
            return;
        }

        if (classSymbol.IsAbstract)
        {
            return;
        }

        var iEntityFinalizerInterface = context.SemanticModel.Compilation.GetTypeByMetadataName(IEntityFinalizerMetadataName);
        if (iEntityFinalizerInterface is null)
        {
            return;
        }

        var implementedEntityFinalizerInterface = classSymbol.AllInterfaces
            .FirstOrDefault(i => i.IsGenericType && SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iEntityFinalizerInterface));

        var entityTypeSymbol = implementedEntityFinalizerInterface?.TypeArguments.FirstOrDefault();

        if (entityTypeSymbol == null)
        {
            return;
        }

        Finalizer.Add((classDeclarationSyntax, entityTypeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }
}
