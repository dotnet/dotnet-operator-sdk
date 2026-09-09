// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using FluentAssertions;

using k8s.Models;

using KubeOps.Cli.Generators;
using KubeOps.Cli.Transpilation;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KubeOps.Cli.Test.Transpilation;

[Trait("Area", "Transpilation")]
public sealed class AssemblyLoaderTest
{
    [Fact]
    public void Should_Not_Inspect_Assemblies_Loaded_From_The_Cli_Directory()
    {
        using var parser = CreateParser();

        // Utilities.GetContextType falls back to LoadFromAssemblyPath for types that only exist in the CLI.
        parser.LoadFromAssemblyPath(typeof(RbacGenerator).Assembly.Location);

        var inspect = () =>
        {
            parser.GetRbacAttributes().ToList();
            parser.GetValidatedEntities().ToList();
            parser.GetMutatedEntities().ToList();
            parser.GetConvertedEntities().ToList();
        };

        inspect.Should().NotThrow();
        parser.GetEntities().Should().ContainSingle().Which.Name.Should().Be("AssemblyLoaderTestEntity");
    }

    private static MetadataLoadContext CreateParser()
    {
        const string source = """
            using k8s.Models;

            [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "AssemblyLoaderTestEntity")]
            internal sealed class AssemblyLoaderTestEntity;
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "AssemblyLoaderTestEntity.cs");
        var referencePaths = GetReferencePaths().ToList();
        var compilation = CSharpCompilation.Create(
            $"AssemblyLoaderTestAssembly-{Guid.NewGuid():N}",
            [syntaxTree],
            referencePaths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        compilation.Emit(assemblyStream).Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        // The resolver mirrors production: it only knows the inspected project's references, so resolving
        // anything the CLI itself is compiled against fails.
        var parser = new MetadataLoadContext(new PathAssemblyResolver(referencePaths
            .Where(path => !Path.GetFileName(path).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))));
        parser.LoadFromByteArray(assemblyStream.ToArray());
        return parser;
    }

    private static IEnumerable<string> GetReferencePaths()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");

        return trustedPlatformAssemblies.Split(Path.PathSeparator)
            .Concat([typeof(V1Namespace).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
