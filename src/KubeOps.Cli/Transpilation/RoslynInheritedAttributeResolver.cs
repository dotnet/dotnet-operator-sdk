// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;

using KubeOps.Transpiler;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KubeOps.Cli.Transpilation;

/// <summary>
/// Resolves inherited attribute values via the Roslyn semantic model. The CLI must not load or
/// execute the user's assembly, so reflection-based resolution is not an option here. Instead the
/// already-built <see cref="Compilation"/> is queried: the parameterless constructor's
/// <c>: base(...)</c> initializer is located in source, its arguments are read as compile-time
/// constants, and each constant is mapped to the base-constructor parameter — and therefore the
/// property — it initializes. No user code is executed and no IL is parsed.
/// </summary>
internal sealed class RoslynInheritedAttributeResolver(IReadOnlyList<Compilation> compilations)
    : IInheritedAttributeResolver
{
    public bool TryResolve(Type attributeType, out IReadOnlyDictionary<string, object?> propertyValues)
    {
        propertyValues = ReadOnlyDictionary<string, object?>.Empty;

        if (attributeType.FullName is not { } fullName)
        {
            return false;
        }

        foreach (var compilation in compilations)
        {
            if (compilation.GetTypeByMetadataName(fullName) is not { } symbol)
            {
                continue;
            }

            var ctor = symbol.InstanceConstructors.FirstOrDefault(c => c.Parameters.IsEmpty);
            if (ctor?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not ConstructorDeclarationSyntax { Initializer.ArgumentList.Arguments.Count: > 0 } decl)
            {
                continue;
            }

            var model = compilation.GetSemanticModel(decl.SyntaxTree);
            if (model.GetSymbolInfo(decl.Initializer!).Symbol is not IMethodSymbol baseCtor)
            {
                continue;
            }

            var values = ResolveArguments(model, decl.Initializer!.ArgumentList.Arguments, baseCtor, symbol);
            if (values.Count > 0)
            {
                propertyValues = values;
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, object?> ResolveArguments(
        SemanticModel model,
        IReadOnlyList<ArgumentSyntax> arguments,
        IMethodSymbol baseCtor,
        INamedTypeSymbol attributeSymbol)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var constant = model.GetConstantValue(argument.Expression);
            if (!constant.HasValue)
            {
                continue;
            }

            var parameterName = argument.NameColon?.Name.Identifier.ValueText
                ?? (i < baseCtor.Parameters.Length ? baseCtor.Parameters[i].Name : null);
            if (FindProperty(attributeSymbol, parameterName) is not { } property)
            {
                continue;
            }

            values[property.Name] = constant.Value;
        }

        return values;
    }

    private static IPropertySymbol? FindProperty(INamedTypeSymbol attributeSymbol, string? parameterName)
    {
        if (parameterName is null)
        {
            return null;
        }

        for (INamedTypeSymbol? current = attributeSymbol; current is not null; current = current.BaseType)
        {
            var match = current.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
