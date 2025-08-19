// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;

namespace KubeOps.Generator.SyntaxReceiver;

internal sealed class CombinedSyntaxReceiver(params ISyntaxContextReceiver[] receivers) : ISyntaxContextReceiver
{
    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        foreach (var syntaxContextReceiver in receivers)
        {
            syntaxContextReceiver.OnVisitSyntaxNode(context);
        }
    }
}
