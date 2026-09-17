// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Cli;
using KubeOps.Cli.Transpilation;

namespace KubeOps.Cli.Test.Transpilation;

[Trait("Area", "Rbac")]
public sealed class OperatorRbacTargetResolverTest
{
    [Fact]
    public void Should_Use_Discovered_Namespace_When_Scope_Is_Auto()
    {
        var target = OperatorRbacTargetResolver.Resolve(
            RbacScope.Auto,
            OperatorWatchScope.Namespaced("tenant-a"),
            null,
            "demo");

        target.Scope.Kind.Should().Be(OperatorWatchScopeKind.Namespaced);
        target.Namespace.Should().Be("tenant-a");
        target.GenerateNamespace.Should().BeFalse();
        target.ReportDiagnostics.Should().BeTrue();
    }

    [Fact]
    public void Should_Generate_Cluster_Wide_Rbac_When_Discovery_Is_Unknown()
    {
        var target = OperatorRbacTargetResolver.Resolve(
            RbacScope.Auto,
            OperatorWatchScope.Unknown(new OperatorWatchScopeDiagnostic("dynamic")),
            null,
            "demo");

        target.Scope.Kind.Should().Be(OperatorWatchScopeKind.ClusterWide);
        target.Namespace.Should().Be("demo-system");
        target.GenerateNamespace.Should().BeTrue();
        target.ReportDiagnostics.Should().BeTrue();
    }

    [Fact]
    public void Should_Generate_Namespaced_Rbac_For_Dynamic_Namespace_When_Requested()
    {
        var target = OperatorRbacTargetResolver.Resolve(
            RbacScope.Namespaced,
            OperatorWatchScope.Unknown(new OperatorWatchScopeDiagnostic("dynamic")),
            "tenant-a",
            "demo");

        target.Scope.Kind.Should().Be(OperatorWatchScopeKind.Namespaced);
        target.Namespace.Should().Be("tenant-a");
        target.GenerateNamespace.Should().BeFalse();
        target.ReportDiagnostics.Should().BeFalse();
    }

    [Fact]
    public void Should_Fall_Back_To_System_Namespace_When_Namespaced_Without_Namespace_Option()
    {
        var target = OperatorRbacTargetResolver.Resolve(
            RbacScope.Namespaced,
            OperatorWatchScope.ClusterWide,
            null,
            "demo");

        target.Scope.Kind.Should().Be(OperatorWatchScopeKind.Namespaced);
        target.Namespace.Should().Be("demo-system");
        target.GenerateNamespace.Should().BeTrue();
    }

    [Fact]
    public void Should_Generate_Cluster_Wide_Rbac_When_Requested_Despite_Static_Namespace()
    {
        var target = OperatorRbacTargetResolver.Resolve(
            RbacScope.Cluster,
            OperatorWatchScope.Namespaced("tenant-a"),
            null,
            "demo");

        target.Scope.Kind.Should().Be(OperatorWatchScopeKind.ClusterWide);
        target.Namespace.Should().Be("demo-system");
        target.GenerateNamespace.Should().BeTrue();
        target.ReportDiagnostics.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_Namespace_Option_Differs_From_Static_Namespace()
    {
        var resolve = () => OperatorRbacTargetResolver.Resolve(
            RbacScope.Auto,
            OperatorWatchScope.Namespaced("tenant-a"),
            "tenant-b",
            "demo");

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*tenant-b*tenant-a*");
    }
}
