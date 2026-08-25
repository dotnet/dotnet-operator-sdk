// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Cli.Transpilation;
using KubeOps.Operator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace KubeOps.Cli.Test.Transpilation;

[Trait("Area", "Rbac")]
public class OperatorWatchScopeDiscoveryTest
{
    [Fact]
    public void Should_Detect_Cluster_Wide_Registration_Without_Configuration()
    {
        var scope = Discover("services.AddKubernetesOperator();");

        scope.Kind.Should().Be(OperatorWatchScopeKind.ClusterWide);
        scope.Namespace.Should().BeNull();
    }

    [Fact]
    public void Should_Detect_Namespace_From_Assignment()
    {
        var scope = Discover("services.AddKubernetesOperator(settings => settings.Namespace = \"tenant-a\");");

        scope.Kind.Should().Be(OperatorWatchScopeKind.Namespaced);
        scope.Namespace.Should().Be("tenant-a");
    }

    [Fact]
    public void Should_Detect_Namespace_From_Constant()
    {
        var scope = Discover(
            """
            const string WatchNamespace = "tenant-a";
            services.AddKubernetesOperator(settings =>
            {
                settings.Namespace = WatchNamespace;
                settings.LeaderElectionType = LeaderElectionType.Single;
            });
            """);

        scope.Kind.Should().Be(OperatorWatchScopeKind.Namespaced);
        scope.Namespace.Should().Be("tenant-a");
    }

    [Fact]
    public void Should_Detect_Namespace_From_Fluent_Configuration()
    {
        var scope = Discover(
            "services.AddKubernetesOperator(settings => settings.WithName(\"test\").WithNamespace(\"tenant-a\"));");

        scope.Kind.Should().Be(OperatorWatchScopeKind.Namespaced);
        scope.Namespace.Should().Be("tenant-a");
    }

    [Fact]
    public void Should_Detect_Cluster_Wide_Registration_When_Other_Settings_Are_Configured()
    {
        var scope = Discover(
            "services.AddKubernetesOperator(settings => settings.LeaderElectionType = LeaderElectionType.Single);");

        scope.Kind.Should().Be(OperatorWatchScopeKind.ClusterWide);
    }

    [Fact]
    public void Should_Report_Dynamic_Namespace_As_Unknown()
    {
        var scope = Discover(
            """
            var configuredNamespace = System.Environment.GetEnvironmentVariable("OPERATOR_NAMESPACE");
            services.AddKubernetesOperator(settings => settings.Namespace = configuredNamespace);
            """);

        scope.Kind.Should().Be(OperatorWatchScopeKind.Unknown);
        scope.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Contain("compile-time constant");
    }

    [Fact]
    public void Should_Report_Method_Group_As_Unknown()
    {
        var scope = Discover(
            """
            services.AddKubernetesOperator(Configure);

            static void Configure(OperatorSettingsBuilder settings) => settings.Namespace = "tenant-a";
            """);

        scope.Kind.Should().Be(OperatorWatchScopeKind.Unknown);
        scope.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Contain("inline lambda");
    }

    [Fact]
    public void Should_Report_Aliased_Settings_Builder_As_Unknown()
    {
        var scope = Discover(
            """
            services.AddKubernetesOperator(settings =>
            {
                var alias = settings;
                alias.Namespace = "tenant-a";
            });
            """);

        scope.Kind.Should().Be(OperatorWatchScopeKind.Unknown);
        scope.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Contain("cannot be evaluated statically");
    }

    [Fact]
    public void Should_Report_Conflicting_Registrations_As_Unknown()
    {
        var scope = Discover(
            """
            services.AddKubernetesOperator(settings => settings.Namespace = "tenant-a");
            services.AddKubernetesOperator(settings => settings.Namespace = "tenant-b");
            """);

        scope.Kind.Should().Be(OperatorWatchScopeKind.Unknown);
        scope.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Contain("different watch namespaces");
    }

    private static OperatorWatchScope Discover(string statements)
    {
        var source = $$"""
            using KubeOps.Abstractions.Builder;
            using KubeOps.Operator;
            using Microsoft.Extensions.DependencyInjection;

            IServiceCollection services = new ServiceCollection();
            {{statements}}
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "Program.cs");
        var compilation = CSharpCompilation.Create(
            "OperatorWatchScopeTest",
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        return OperatorWatchScopeDiscovery.Discover([compilation]);
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var paths = trustedPlatformAssemblies.Split(Path.PathSeparator).Concat(new[]
        {
            typeof(OperatorSettingsBuilder).Assembly.Location,
            typeof(KubeOps.Operator.ServiceCollectionExtensions).Assembly.Location,
            typeof(IServiceCollection).Assembly.Location,
            typeof(ServiceCollection).Assembly.Location,
        });

        return paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
    }
}
