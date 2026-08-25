// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Cli.Generators;

namespace KubeOps.Cli.Test.Generators;

[Trait("Area", "Rbac")]
public class RbacGeneratorTest
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
}
