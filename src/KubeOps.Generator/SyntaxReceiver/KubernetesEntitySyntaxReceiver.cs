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
    private const string PluralName = "PluralName";
    private const string VersionName = "ApiVersion";
    private const string DefaultVersion = "v1";

    public List<AttributedEntity> Entities { get; } = [];

    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax { AttributeLists.Count: > 0 } cls ||
            cls.AttributeLists.SelectMany(a => a.Attributes)
                .FirstOrDefault(a => a.Name.ToString() == "KubernetesEntity") is not { } attr)
        {
            return;
        }

        Entities.Add(new(
            cls,
            GetArgumentValue(cls, attr, KindName) ?? cls.Identifier.ToString(),
            GetArgumentValue(cls, attr, VersionName) ?? DefaultVersion,
            GetArgumentValue(cls, attr, GroupName),
            GetArgumentValue(cls, attr, PluralName)));
    }

    private static string? GetArgumentValue(ClassDeclarationSyntax cls, AttributeSyntax attr, string argName)
    {
        var argument = attr.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.ToString() == argName);
        if (argument != null)
        {
            if (argument.Expression is LiteralExpressionSyntax { Token.ValueText: { } value })
            {
                return value;
            }

            if (argument.Expression is IdentifierNameSyntax { Identifier.ValueText: { } ident })
            {
                var field = cls.Members
                    .OfType<FieldDeclarationSyntax>()
                    .FirstOrDefault(f => f.Modifiers.Any(SyntaxKind.ConstKeyword) &&
                                         f.Declaration.Variables.Any(v => v.Identifier.ValueText == ident));
                if (field != null)
                {
                    var variable = field.Declaration.Variables
                                        .First(v => v.Identifier.ValueText == ident);
                    if (variable.Initializer?.Value is LiteralExpressionSyntax { Token.ValueText: { } fieldValue })
                    {
                        return fieldValue;
                    }
                }
            }
        }

        return null;
    }
}
