// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KubeOps.Generator.Discovery;

internal static class EntityDiscovery
{
    private const string KubernetesEntitySyntaxName = "KubernetesEntity";
    private const string KubernetesEntityAttributeName = "KubernetesEntityAttribute";
    private const string IEntityControllerMetadataName = "KubeOps.Abstractions.Reconciliation.Controller.IEntityController`1";
    private const string IEntityFinalizerMetadataName = "KubeOps.Abstractions.Reconciliation.Finalizer.IEntityFinalizer`1";
    private const string LabelSelectorSyntaxName = "LabelSelector";
    private const string FieldSelectorSyntaxName = "FieldSelector";

    private const string KindName = "Kind";
    private const string GroupName = "Group";
    private const string PluralName = "PluralName";
    private const string VersionName = "ApiVersion";
    private const string DefaultVersion = "v1";

    public static IncrementalValueProvider<EquatableArray<AttributedEntity>> GetEntities(
        IncrementalGeneratorInitializationContext context)
    {
        var localEntities = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetLocalEntity(ctx))
            .Where(static e => e is not null)
            .Select(static (e, _) => e!.Value);

        var referencedEntities = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => GetReferencedEntities(ctx))
            .Where(static a => !a.IsDefaultOrEmpty);

        return localEntities.Collect()
            .Combine(referencedEntities.Collect())
            .Select(static (pair, _) => Merge(pair.Left, pair.Right));
    }

    public static IncrementalValueProvider<EquatableArray<ControllerRegistration>> GetControllers(
        IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => GetController(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!.Value)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<ControllerRegistration>(arr.Distinct().ToImmutableArray()));

    public static IncrementalValueProvider<EquatableArray<FinalizerRegistration>> GetFinalizers(
        IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => GetFinalizer(ctx))
            .Where(static f => f is not null)
            .Select(static (f, _) => f!.Value)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<FinalizerRegistration>(arr.Distinct().ToImmutableArray()));

    private static EquatableArray<AttributedEntity> Merge(
        ImmutableArray<AttributedEntity> local,
        ImmutableArray<ImmutableArray<AttributedEntity>> referenced)
    {
        // Among entities discovered via usage, the first occurrence per type wins.
        var map = referenced
            .SelectMany(group => group)
            .GroupBy(e => e.ClassDeclaration.FullyQualifiedName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Locally attributed entities take precedence over entities discovered via usage.
        foreach (var entity in local)
        {
            map[entity.ClassDeclaration.FullyQualifiedName] = entity;
        }

        var ordered = map.Values
            .OrderBy(e => e.ClassDeclaration.FullyQualifiedName, StringComparer.Ordinal)
            .ToImmutableArray();

        return new(ordered);
    }

    private static AttributedEntity? GetLocalEntity(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax cls)
        {
            return null;
        }

        // Match the attribute syntactically (by name) so entities are discovered even when the
        // KubernetesEntity attribute does not bind to a symbol in the input compilation.
        var attr = cls.AttributeLists
            .SelectMany(a => a.Attributes)
            .FirstOrDefault(a => a.Name.ToString() == KubernetesEntitySyntaxName);

        if (attr is null ||
            ModelExtensions.GetDeclaredSymbol(context.SemanticModel, cls) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        return new AttributedEntity(
            new(
                ClassName: cls.Identifier.Text,
                FullyQualifiedName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Namespace: symbol.ContainingNamespace.IsGlobalNamespace
                    ? null
                    : symbol.ContainingNamespace.ToDisplayString(),
                ModifierKinds: new(
                    cls.Modifiers.Select(m => m.Kind()).ToImmutableArray()),
                IsPartial: cls.Modifiers.Any(SyntaxKind.PartialKeyword),
                HasParameterlessConstructor: cls.Members.Any(m
                    => m is ConstructorDeclarationSyntax { ParameterList.Parameters.Count: 0 }),
                IsFromReferencedAssembly: false),
            Kind: GetArgumentValue(context.SemanticModel, attr, KindName) ?? cls.Identifier.Text,
            Version: GetArgumentValue(context.SemanticModel, attr, VersionName) ?? DefaultVersion,
            Group: GetArgumentValue(context.SemanticModel, attr, GroupName),
            Plural: GetArgumentValue(context.SemanticModel, attr, PluralName));
    }

    private static string? GetArgumentValue(SemanticModel model, AttributeSyntax attr, string argName)
    {
        var expr = attr.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.ToString() == argName)?.Expression;

        if (expr is null)
        {
            return null;
        }

        if (model.GetConstantValue(expr) is { HasValue: true, Value: string s })
        {
            return s;
        }

        return expr is LiteralExpressionSyntax literal ? literal.Token.ValueText : null;
    }

    private static ImmutableArray<AttributedEntity> GetReferencedEntities(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDecl ||
            ModelExtensions.GetDeclaredSymbol(context.SemanticModel, classDecl) is not INamedTypeSymbol symbol)
        {
            return ImmutableArray<AttributedEntity>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<AttributedEntity>();
        foreach (var @interface in symbol.AllInterfaces.Where(i =>
                     (i.Name is "IEntityController" or "IEntityFinalizer")
                     && i is { IsGenericType: true, TypeArguments.Length: > 0 }))
        {
            if (@interface.TypeArguments[0] is not INamedTypeSymbol entityType)
            {
                continue;
            }

            var entity = BuildEntityFromSymbol(entityType);
            if (entity is not null)
            {
                builder.Add(entity.Value);
            }
        }

        return builder.ToImmutable();
    }

    private static AttributedEntity? BuildEntityFromSymbol(INamedTypeSymbol symbol)
    {
        var attr = symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == KubernetesEntityAttributeName);

        if (attr is null)
        {
            return null;
        }

        return new AttributedEntity(
            new(
                ClassName: symbol.Name,
                FullyQualifiedName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Namespace: symbol.ContainingNamespace.IsGlobalNamespace
                    ? null
                    : symbol.ContainingNamespace.ToDisplayString(),
                ModifierKinds: EquatableArray<SyntaxKind>.Empty,
                IsPartial: false,
                HasParameterlessConstructor: symbol.Constructors
                    .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public),
                IsFromReferencedAssembly: true),
            Kind: GetAttributeValue(attr, KindName) ?? symbol.Name,
            Version: GetAttributeValue(attr, VersionName) ?? DefaultVersion,
            Group: GetAttributeValue(attr, GroupName),
            Plural: GetAttributeValue(attr, PluralName));
    }

    private static ControllerRegistration? GetController(GeneratorSyntaxContext context)
    {
        var entity = GetImplementedEntity(context, IEntityControllerMetadataName, out var classSymbol);
        return entity is null
            ? null
            : new ControllerRegistration(
                classSymbol!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                entity,
                GetSelectorAttributeType(context, LabelSelectorSyntaxName),
                GetSelectorAttributeType(context, FieldSelectorSyntaxName),
                LocationInfo.CreateFrom(context.Node));
    }

    // Match the attribute syntactically by name (consistent with the KubernetesEntity attribute handling
    // above) so selectors are discovered even when the attribute does not fully bind in the input
    // compilation; the typeof() argument is then resolved through the semantic model.
    private static string? GetSelectorAttributeType(GeneratorSyntaxContext context, string attributeSyntaxName)
    {
        if (context.Node is not ClassDeclarationSyntax cls)
        {
            return null;
        }

        var attribute = cls.AttributeLists
            .SelectMany(a => a.Attributes)
            .FirstOrDefault(a => a.Name.ToString().Split('.') is var parts &&
                                 parts[parts.Length - 1] is var name &&
                                 (name == attributeSyntaxName || name == attributeSyntaxName + "Attribute"));

        if (attribute?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not TypeOfExpressionSyntax typeOfExpression)
        {
            return null;
        }

        return ModelExtensions.GetTypeInfo(context.SemanticModel, typeOfExpression.Type).Type is INamedTypeSymbol selectorType
            ? selectorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;
    }

    private static FinalizerRegistration? GetFinalizer(GeneratorSyntaxContext context)
    {
        var entity = GetImplementedEntity(context, IEntityFinalizerMetadataName, out var classSymbol);
        return entity is null
            ? null
            : new FinalizerRegistration(
                classSymbol!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                classSymbol.Name,
                entity);
    }

    private static string? GetImplementedEntity(
        GeneratorSyntaxContext context,
        string interfaceMetadataName,
        out INamedTypeSymbol? classSymbol)
    {
        classSymbol = null;

        if (context.Node is not ClassDeclarationSyntax classDeclarationSyntax ||
            context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax) is not { } symbol ||
            symbol.IsAbstract)
        {
            return null;
        }

        var interfaceSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(interfaceMetadataName);
        if (interfaceSymbol is null)
        {
            return null;
        }

        var implemented = symbol.AllInterfaces.FirstOrDefault(i =>
            i.IsGenericType && SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, interfaceSymbol));

        if (implemented?.TypeArguments.FirstOrDefault() is not { } entityTypeSymbol)
        {
            return null;
        }

        classSymbol = symbol;
        return entityTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

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
}
