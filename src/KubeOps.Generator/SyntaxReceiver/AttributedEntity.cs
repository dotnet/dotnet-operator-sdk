// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KubeOps.Generator.SyntaxReceiver;

internal record struct AttributedEntity(
    ClassDeclarationSyntax Class,
    string Kind,
    string Version,
    string? Group,
    string? Plural);
