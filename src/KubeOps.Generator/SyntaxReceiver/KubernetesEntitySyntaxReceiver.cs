// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KubeOps.Generator.SyntaxReceiver;

internal sealed class KubernetesEntitySyntaxReceiver : ISyntaxContextReceiver
{
    private const string KindName = "Kind";
    private const string GroupName = "Group";
    private const string PluralName = "Plural";
    private const string VersionName = "ApiVersion";
    private const string DefaultVersion = "v1";

    public List<AttributedEntity> Entities { get; } = [];

#pragma warning disable RS1024
    private HashSet<INamedTypeSymbol> DiscoveredEntities { get; } = new(SymbolEqualityComparer.Default);
#pragma warning restore RS1024

    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        // 1. entities from assembly (attributed)
        if (context.Node is ClassDeclarationSyntax { AttributeLists.Count: > 0 } cls)
        {
            DiscoverEntitiesFromAssembly(context, cls);
        }

        // 2. referenced entities from controllers and finalizers
        if (context.Node is ClassDeclarationSyntax classDecl)
        {
            DiscoverEntitiesFromReferencedAssembliesByUsage(context, classDecl);
        }
    }

    private static bool IsEntityController(INamedTypeSymbol type)
        => type.Name is "IEntityController";

    private static bool IsEntityFinalizer(INamedTypeSymbol type)
        => type.Name is "IEntityFinalizer";

    private static string? GetAttributeValue(AttributeData attr, string argName)
    {
        var namedArg = attr.NamedArguments.FirstOrDefault(a => a.Key == argName);
        if (namedArg.Value.Value is string s)
        {
            return s;
        }

        var param = attr.AttributeConstructor?.Parameters
            .Select((p, i) => (p, i))
            .FirstOrDefault(x => x.p.Name == argName);

        if (param?.i < attr.ConstructorArguments.Length && attr.ConstructorArguments[param.Value.i].Value is string value)
        {
            return value;
        }

        return null;
    }

    private static string? GetArgumentValue(SemanticModel model, AttributeSyntax attr, string argName)
    {
        if (attr.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.ToString() == argName)
            is not { Expression: { } expr })
        {
            return null;
        }

        if (model.GetConstantValue(expr) is { HasValue: true, Value: string s })
        {
            return s;
        }

        return expr is LiteralExpressionSyntax { Token.ValueText: { } value }
            ? value
            : null;
    }

    private void DiscoverEntitiesFromAssembly(GeneratorSyntaxContext context, ClassDeclarationSyntax cls)
    {
        var attr = cls.AttributeLists
            .SelectMany(a => a.Attributes)
            .FirstOrDefault(a => a.Name.ToString() == "KubernetesEntity");

        if (attr is null)
        {
            return;
        }

        var typeSymbol = ModelExtensions.GetDeclaredSymbol(context.SemanticModel, cls);
        if (typeSymbol is not INamedTypeSymbol namedTypeSymbol)
        {
            return;
        }

        var fullyQualifiedName = namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // if the entity was already discovered via another controller/finalizer,
        // remove it to register the entity as coming from the same assembly
        if (!DiscoveredEntities.Add(namedTypeSymbol))
        {
            var entity = Entities
                .First(e => e.ClassDeclaration.FullyQualifiedName == fullyQualifiedName);
            Entities.Remove(entity);
        }

        Entities.Add(
            new(
                new(
                    ClassName: cls.Identifier.ToString(),
                    FullyQualifiedName: fullyQualifiedName,
                    Namespace: namedTypeSymbol.ContainingNamespace.IsGlobalNamespace
                        ? null
                        : namedTypeSymbol.ContainingNamespace.ToDisplayString(),
                    Modifiers: cls.Modifiers,
                    IsPartial: cls.Modifiers.Any(SyntaxKind.PartialKeyword),
                    HasParameterlessConstructor: cls.Members.Any(m
                        => m is ConstructorDeclarationSyntax { ParameterList.Parameters.Count: 0 }),
                    IsFromReferencedAssembly: false),
                Kind: GetArgumentValue(context.SemanticModel, attr, KindName) ?? cls.Identifier.ToString(),
                Version: GetArgumentValue(context.SemanticModel, attr, VersionName) ?? DefaultVersion,
                Group: GetArgumentValue(context.SemanticModel, attr, GroupName),
                Plural: GetArgumentValue(context.SemanticModel, attr, PluralName)));
    }

    private void DiscoverEntitiesFromReferencedAssembliesByUsage(GeneratorSyntaxContext context, ClassDeclarationSyntax classDecl)
    {
        var symbol = ModelExtensions.GetDeclaredSymbol(context.SemanticModel, classDecl);
        if (symbol is not INamedTypeSymbol namedTypeSymbol)
        {
            return;
        }

        foreach (var @interface in namedTypeSymbol.AllInterfaces.Where(i =>
                     (IsEntityController(i) || IsEntityFinalizer(i))
                     && i is { IsGenericType: true, TypeArguments.Length: > 0 }))
        {
            if (@interface.TypeArguments[0] is not INamedTypeSymbol entityType || !DiscoveredEntities.Add(entityType))
            {
                continue;
            }

            AddEntityFromSymbol(entityType);
        }
    }

    private void AddEntityFromSymbol(INamedTypeSymbol namedTypeSymbol)
    {
        var attr = namedTypeSymbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "KubernetesEntityAttribute");

        if (attr is null)
        {
            return;
        }

        var kind = GetAttributeValue(attr, KindName) ?? namedTypeSymbol.Name;
        var version = GetAttributeValue(attr, VersionName) ?? DefaultVersion;
        var group = GetAttributeValue(attr, GroupName);
        var plural = GetAttributeValue(attr, PluralName);

        Entities.Add(
            new(
                new(
                    ClassName: namedTypeSymbol.Name,
                    FullyQualifiedName: namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Namespace: namedTypeSymbol.ContainingNamespace.IsGlobalNamespace
                        ? null
                        : namedTypeSymbol.ContainingNamespace.ToDisplayString(),
                    Modifiers: null,
                    IsPartial: false,
                    HasParameterlessConstructor: namedTypeSymbol.Constructors
                        .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public),
                    IsFromReferencedAssembly: true),
                Kind: kind,
                Version: version,
                Group: group,
                Plural: plural));
    }
}
