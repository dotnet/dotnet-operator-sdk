// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Cli.Generators;
using KubeOps.Cli.Transpilation;
using KubeOps.Operator.Web.Webhooks.Admission.Validation;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KubeOps.Cli.Test.Transpilation;

[Trait("Area", "Transpilation")]
public sealed class AssemblyLoaderTest
{
    [Fact]
    public void Should_Not_Inspect_Assemblies_Loaded_From_The_Cli_Directory()
    {
        using var parser = CreateParser("""
            using k8s.Models;

            [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "AssemblyLoaderTestEntity")]
            internal sealed class AssemblyLoaderTestEntity;
            """);

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

    [Fact]
    public void Should_Find_Webhooks_Derived_From_An_Intermediate_Base_Class()
    {
        using var parser = CreateParser("""
            using k8s;
            using k8s.Models;

            using KubeOps.Abstractions.Entities;
            using KubeOps.Operator.Web.Webhooks.Admission.Mutation;
            using KubeOps.Operator.Web.Webhooks.Admission.Validation;

            [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "WebhookTestEntity")]
            public sealed class V1WebhookTestEntity : CustomKubernetesEntity;

            public abstract class AuditedValidationWebhook<TEntity> : ValidationWebhook<TEntity>
                where TEntity : IKubernetesObject<V1ObjectMeta>;

            public sealed class IndirectValidator : AuditedValidationWebhook<V1WebhookTestEntity>
            {
                public override ValidationResult Create(V1WebhookTestEntity entity, bool dryRun) => Success();
            }

            public abstract class AuditedMutationWebhook<TEntity> : MutationWebhook<TEntity>
                where TEntity : IKubernetesObject<V1ObjectMeta>;

            public sealed class IndirectMutator : AuditedMutationWebhook<V1WebhookTestEntity>
            {
                public override MutationResult<V1WebhookTestEntity> Update(
                    V1WebhookTestEntity oldEntity,
                    V1WebhookTestEntity newEntity,
                    bool dryRun) => NoChanges();
            }
            """);

        var validator = parser.GetValidatedEntities().Should().ContainSingle().Subject;
        validator.Webhook.Name.Should().Be("IndirectValidator");
        validator.Metadata.Kind.Should().Be("WebhookTestEntity");
        validator.WebhookPath.Should().Be("/validate/v1webhooktestentity");
        validator.GetOperations().Should().Equal("CREATE");

        var mutator = parser.GetMutatedEntities().Should().ContainSingle().Subject;
        mutator.Webhook.Name.Should().Be("IndirectMutator");
        mutator.Metadata.Kind.Should().Be("WebhookTestEntity");
        mutator.WebhookPath.Should().Be("/mutate/v1webhooktestentity");
        mutator.GetOperations().Should().Equal("UPDATE");
    }

    private static MetadataLoadContext CreateParser(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "AssemblyLoaderTest.cs");
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
            .Concat([
                typeof(V1Namespace).Assembly.Location,
                typeof(CustomKubernetesEntity).Assembly.Location,
                typeof(ValidationWebhook<>).Assembly.Location,
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
