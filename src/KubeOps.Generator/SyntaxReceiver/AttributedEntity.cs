// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Generator.SyntaxReceiver;

internal record struct AttributedEntity(
    ClassDeclarationMetaData ClassDeclaration,
    string Kind,
    string Version,
    string? Group,
    string? Plural);
