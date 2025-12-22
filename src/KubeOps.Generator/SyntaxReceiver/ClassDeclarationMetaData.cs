// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;

namespace KubeOps.Generator.SyntaxReceiver;

internal record struct ClassDeclarationMetaData(
    string ClassName,
    string FullyQualifiedName,
    string? Namespace,
    SyntaxTokenList? Modifiers,
    bool IsPartial,
    bool HasParameterlessConstructor,
    bool IsFromReferencedAssembly);
