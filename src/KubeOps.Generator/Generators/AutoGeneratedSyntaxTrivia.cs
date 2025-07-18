// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KubeOps.Generator.Generators;

public static class AutoGeneratedSyntaxTrivia
{
    public static readonly SyntaxTriviaList Instance =
        new(
            SyntaxFactory.Comment(
                """
                // <auto-generated>
                // This code was generated by a tool.
                // Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                // </auto-generated>
                """),
            SyntaxFactory.CarriageReturnLineFeed,
            SyntaxFactory.Trivia(
                SyntaxFactory.PragmaWarningDirectiveTrivia(
                    SyntaxFactory.Token(SyntaxKind.DisableKeyword),
                    SyntaxFactory.SeparatedList<ExpressionSyntax>(new SyntaxNodeOrTokenList(SyntaxFactory.IdentifierName("CS1591"))),
                    false)),
            SyntaxFactory.CarriageReturnLineFeed);
}
