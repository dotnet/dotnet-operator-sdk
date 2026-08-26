// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Rbac;
using KubeOps.Cli.Generators;
using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Spectre.Console.Testing;

namespace KubeOps.Cli.Test.Generators;

[Trait("Area", "Rbac")]
public sealed class RbacGeneratorTest
{
    [Fact]
    public void Should_Create_Namespaced_Role()
    {
        var rules = new List<V1PolicyRule>
        {
            new()
            {
                ApiGroups = ["example.com"],
                Resources = ["examples"],
                Verbs = ["get", "list", "watch"],
            },
        };

        var role = RbacGenerator.CreateNamespacedRole(rules, "tenant-a");

        role.Kind.Should().Be(V1Role.KubeKind);
        role.Metadata.Name.Should().Be("operator-role");
        role.Metadata.NamespaceProperty.Should().Be("tenant-a");
        role.Rules.Should().BeSameAs(rules);
    }

    [Fact]
    public void Should_Create_Namespaced_Role_Binding()
    {
        var binding = RbacGenerator.CreateNamespacedRoleBinding("tenant-a");

        binding.Kind.Should().Be(V1RoleBinding.KubeKind);
        binding.Metadata.Name.Should().Be("operator-role-binding");
        binding.Metadata.NamespaceProperty.Should().Be("tenant-a");
        binding.RoleRef.ApiGroup.Should().Be(V1Role.KubeGroup);
        binding.RoleRef.Kind.Should().Be(V1Role.KubeKind);
        binding.RoleRef.Name.Should().Be("operator-role");
        binding.Subjects.Should().ContainSingle().Which.Should().BeEquivalentTo(new Rbacv1Subject
        {
            Kind = V1ServiceAccount.KubeKind,
            Name = "default",
            NamespaceProperty = "tenant-a",
        });
    }

    [Fact]
    public void Should_Generate_Namespaced_Rbac_Resources()
    {
        using var parser = CreateParser(
            """
            [GenericRbac(Groups = new[] { "" }, Resources = new[] { "pods" }, Verbs = RbacVerb.Get)]
            """);
        var output = CreateOutput();

        new RbacGenerator(
                parser,
                OutputFormat.Yaml,
                "tenant-a",
                OperatorWatchScope.Namespaced("tenant-a"))
            .Generate(output);

        output.Files.Should().BeEquivalentTo("operator-role.yaml", "operator-role-binding.yaml");
        var role = output["operator-role.yaml"].Should().BeOfType<V1Role>().Subject;
        role.Metadata.NamespaceProperty.Should().Be("tenant-a");
        role.Rules.Should().Contain(rule => rule.Resources!.Contains("pods"));
        KubernetesYaml.Deserialize<V1Role>(KubernetesYaml.Serialize(role))
            .Should().BeEquivalentTo(role);
        output["operator-role-binding.yaml"].Should().BeOfType<V1RoleBinding>();
    }

    [Fact]
    public void Should_Generate_Cluster_Wide_Rbac_Resources()
    {
        using var parser = CreateParser(
            """
            [GenericRbac(Groups = new[] { "" }, Resources = new[] { "pods" }, Verbs = RbacVerb.Get)]
            """);
        var output = CreateOutput();

        new RbacGenerator(parser, OutputFormat.Yaml, "operator-system", OperatorWatchScope.ClusterWide)
            .Generate(output);

        output["operator-role.yaml"].Should().BeOfType<V1ClusterRole>();
        output["operator-role-binding.yaml"].Should().BeOfType<V1ClusterRoleBinding>();
    }

    [Fact]
    public void Should_Reject_Cluster_Scoped_Entities_For_Namespaced_Rbac()
    {
        using var parser = CreateParser(
            """
            [KubernetesEntity(Group = "example.com", ApiVersion = "v1", Kind = "ClusterEntity")]
            [EntityScope(EntityScope.Cluster)]
            internal sealed class ClusterEntity : CustomKubernetesEntity;

            [EntityRbac(typeof(ClusterEntity), Verbs = RbacVerb.Get)]
            internal sealed class ClusterController;
            """);
        var output = CreateOutput();
        var generator = new RbacGenerator(
            parser,
            OutputFormat.Yaml,
            "tenant-a",
            OperatorWatchScope.Namespaced("tenant-a"));

        var action = () => generator.Generate(output);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*cluster-scoped entities*ClusterEntity*");
    }

    [Fact]
    public void Should_Reject_Non_Resource_Urls_For_Namespaced_Rbac()
    {
        using var parser = CreateParser(
            """
            [GenericRbac(Urls = new[] { "/healthz" }, Verbs = RbacVerb.Get)]
            """);
        var output = CreateOutput();
        var generator = new RbacGenerator(
            parser,
            OutputFormat.Yaml,
            "tenant-a",
            OperatorWatchScope.Namespaced("tenant-a"));

        var action = () => generator.Generate(output);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-resource URLs*");
    }

    private static ResultOutput CreateOutput() => new(new TestConsole(), OutputFormat.Yaml);

    private static MetadataLoadContext CreateParser(string attribute)
    {
        var source = $$"""
            using k8s.Models;
            using KubeOps.Abstractions.Entities;
            using KubeOps.Abstractions.Entities.Attributes;
            using KubeOps.Abstractions.Rbac;

            {{attribute}}
            internal sealed class RbacTestType;
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "RbacTestType.cs");
        var referencePaths = GetReferencePaths().ToList();
        var compilation = CSharpCompilation.Create(
            $"RbacGeneratorTestAssembly-{Guid.NewGuid():N}",
            [syntaxTree],
            referencePaths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var result = compilation.Emit(assemblyStream);
        result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var parser = new MetadataLoadContext(new PathAssemblyResolver(referencePaths));
        parser.LoadFromByteArray(assemblyStream.ToArray());
        return parser;
    }

    private static IEnumerable<string> GetReferencePaths()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");

        return trustedPlatformAssemblies.Split(Path.PathSeparator)
            .Concat(new[]
            {
                typeof(V1Namespace).Assembly.Location,
                typeof(EntityRbacAttribute).Assembly.Location,
                typeof(RbacGenerator).Assembly.Location,
            })
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
