// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KubeOps.Generator.Test;

internal static class TestHelperExtensions
{
    public static Compilation CreateCompilation(this string source)
        => CSharpCompilation.Create(
            "compilation",
            [
                CSharpSyntaxTree.ParseText(source),
            ],
            [
                MetadataReference.CreateFromFile(typeof(Binder).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Abstractions.Controller.IEntityController<>).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(k8s.IKubernetesObject<>).GetTypeInfo().Assembly.Location),
            ],
            new(OutputKind.DynamicallyLinkedLibrary));
}
