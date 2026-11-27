// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Abstractions.Test.Entities;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Entities)]
public sealed class KubernetesExtensionsOwnerReferencesTest
{
    [Fact]
    public void MakeOwnerReference_Leaves_Controller_And_BlockOwnerDeletion_Unset()
    {
        var reference = Deployment("owner", uid: "uid-1").MakeOwnerReference();

        reference.Should().BeEquivalentTo(new V1OwnerReference
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Name = "owner",
            Uid = "uid-1",
        });
    }

    [Fact]
    public void MakeControllerReference_Sets_Controller_And_BlockOwnerDeletion()
    {
        var reference = Deployment("owner", uid: "uid-1").MakeControllerReference();

        reference.Should().BeEquivalentTo(new V1OwnerReference
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Name = "owner",
            Uid = "uid-1",
            Controller = true,
            BlockOwnerDeletion = true,
        });
    }

    [Fact]
    public void MakeOwnerReference_Takes_ApiVersion_And_Kind_From_The_Entity_Type()
    {
        // Items of a list response carry no apiVersion and kind.
        var owner = new V1Deployment { Metadata = new() { Name = "owner", Uid = "uid-1" } };

        owner.MakeOwnerReference().Should().BeEquivalentTo(new { ApiVersion = "apps/v1", Kind = "Deployment" });
        new V1ConfigMap { Metadata = new() { Name = "owner", Uid = "uid-1" } }.MakeOwnerReference()
            .Should().BeEquivalentTo(new { ApiVersion = "v1", Kind = "ConfigMap" });
    }

    [Fact]
    public void MakeObjectReference_Takes_ApiVersion_And_Kind_From_The_Entity_Type()
    {
        var reference = new V1Deployment { Metadata = new() { Name = "owner" } }.MakeObjectReference();

        reference.Should().BeEquivalentTo(new { ApiVersion = "apps/v1", Kind = "Deployment" });
    }

    [Fact]
    public void WithOwnerReference_Keeps_Owners_Of_Different_Kinds_From_A_List_Apart()
    {
        var deployment = new V1Deployment
        {
            Metadata = new() { Name = "x", NamespaceProperty = "default", Uid = "1" },
        };
        var statefulSet = new V1StatefulSet
        {
            Metadata = new() { Name = "x", NamespaceProperty = "default", Uid = "2" },
        };

        var resource = ConfigMap().WithOwnerReference(deployment).WithOwnerReference(statefulSet);

        resource.OwnerReferences().Select(reference => reference.Kind)
            .Should().BeEquivalentTo("Deployment", "StatefulSet");
    }

    [Theory]
    [InlineData(null, "uid")]
    [InlineData("owner", null)]
    public void WithOwnerReference_Throws_For_An_Owner_Without_Name_Or_Uid(string? name, string? uid)
    {
        var owner = new V1Deployment { Metadata = new() { Name = name, NamespaceProperty = "default", Uid = uid } };

        var act = () => ConfigMap().WithOwnerReference(owner);

        act.Should().Throw<ArgumentException>().WithMessage("*needs an apiVersion, kind, name and uid*");
    }

    [Fact]
    public void WithOwnerReference_Adds_The_Owner()
    {
        var resource = ConfigMap().WithOwnerReference(Deployment("owner"));

        resource.OwnerReferences().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Kind = "Deployment", Name = "owner", Controller = (bool?)null });
    }

    [Fact]
    public void WithOwnerReference_Replaces_The_Reference_To_The_Same_Owner()
    {
        var resource = ConfigMap()
            .WithOwnerReference(Deployment("owner", apiVersion: "apps/v1beta1", uid: "old"))
            .WithOwnerReference(Deployment("owner", uid: "new"));

        resource.OwnerReferences().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { ApiVersion = "apps/v1", Uid = "new" });
    }

    [Fact]
    public void WithOwnerReference_Keeps_References_To_Other_Owners()
    {
        var resource = ConfigMap()
            .WithOwnerReference(Deployment("owner"))
            .WithOwnerReference(Deployment("other"))
            .WithOwnerReference(Deployment("owner", apiVersion: "example.com/v1"));

        resource.OwnerReferences().Should().HaveCount(3);
    }

    [Fact]
    public void WithOwnerReference_Replaces_A_Controller_Reference_To_The_Same_Owner()
    {
        var resource = ConfigMap()
            .WithControllerReference(Deployment("owner"))
            .WithOwnerReference(Deployment("owner"));

        resource.OwnerReferences().Should().ContainSingle()
            .Which.Controller.Should().BeNull();
        resource.GetController().Should().BeNull();
    }

    [Fact]
    public void WithControllerReference_Adds_The_Owner_As_Controller()
    {
        var resource = ConfigMap().WithControllerReference(Deployment("owner"));

        resource.OwnerReferences().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Name = "owner", Controller = true, BlockOwnerDeletion = true });
    }

    [Fact]
    public void WithControllerReference_Replaces_A_Plain_Reference_To_The_Same_Owner()
    {
        var resource = ConfigMap()
            .WithOwnerReference(Deployment("owner"))
            .WithControllerReference(Deployment("owner"))
            .WithControllerReference(Deployment("owner"));

        resource.OwnerReferences().Should().ContainSingle()
            .Which.Controller.Should().BeTrue();
    }

    [Fact]
    public void WithControllerReference_Keeps_Other_Owner_References()
    {
        var resource = ConfigMap()
            .WithOwnerReference(Deployment("other"))
            .WithControllerReference(Deployment("owner"));

        resource.OwnerReferences().Should().HaveCount(2);
        resource.GetController()!.Name.Should().Be("owner");
    }

    [Fact]
    public void WithControllerReference_Throws_When_Another_Owner_Is_The_Controller()
    {
        var resource = ConfigMap().WithControllerReference(Deployment("owner"));

        var act = () => resource.WithControllerReference(Deployment("other"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*already controlled by Deployment owner*");
        resource.OwnerReferences().Should().ContainSingle();
    }

    [Fact]
    public void GetController_Returns_Null_Without_Controller()
    {
        ConfigMap().GetController().Should().BeNull();
        ConfigMap().WithOwnerReference(Deployment("owner")).GetController().Should().BeNull();
    }

    [Fact]
    public void Owner_References_Accept_A_Cluster_Scoped_Owner()
    {
        var owner = Deployment("owner", @namespace: null);

        ConfigMap().WithOwnerReference(owner).OwnerReferences().Should().ContainSingle();
        ConfigMap(@namespace: null).WithControllerReference(owner).OwnerReferences().Should().ContainSingle();
    }

    [Theory]
    [InlineData(null, "*has no namespace*")]
    [InlineData("other", "*must be cluster-scoped or in namespace other*")]
    public void WithOwnerReference_Throws_For_An_Owner_In_Another_Namespace(string? @namespace, string message)
    {
        var resource = ConfigMap(@namespace);

        var act = () => resource.WithOwnerReference(Deployment("owner"));

        act.Should().Throw<ArgumentException>().WithMessage(message).Which.ParamName.Should().Be("owner");
        resource.Metadata.OwnerReferences.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "*has no namespace*")]
    [InlineData("other", "*must be cluster-scoped or in namespace other*")]
    public void WithControllerReference_Throws_For_An_Owner_In_Another_Namespace(string? @namespace, string message)
    {
        var act = () => ConfigMap(@namespace).WithControllerReference(Deployment("owner"));

        act.Should().Throw<ArgumentException>().WithMessage(message).Which.ParamName.Should().Be("owner");
    }

    private static V1Deployment Deployment(
        string name,
        string apiVersion = "apps/v1",
        string uid = "uid",
        string? @namespace = "default")
        => new()
        {
            ApiVersion = apiVersion,
            Kind = "Deployment",
            Metadata = new() { Name = name, NamespaceProperty = @namespace, Uid = uid },
        };

    private static V1ConfigMap ConfigMap(string? @namespace = "default")
        => new()
        {
            ApiVersion = "v1",
            Kind = "ConfigMap",
            Metadata = new() { Name = "resource", NamespaceProperty = @namespace },
        };
}
