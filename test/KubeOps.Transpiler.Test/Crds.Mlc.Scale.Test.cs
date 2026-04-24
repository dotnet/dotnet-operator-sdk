// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace KubeOps.Transpiler.Test;

public sealed partial class CrdsMlcTest
{
    [Fact]
    public void Should_Not_Add_Scale_SubResource_If_Absent()
    {
        var crd = _mlc.Transpile(typeof(EntityWithStatus));

        var subresources = crd.Spec.Versions[0].Subresources;
        subresources.Should().NotBeNull();
        subresources.Status.Should().NotBeNull();
        subresources.Scale.Should().BeNull();
    }

    [Fact]
    public void Should_Add_Scale_SubResource_With_Required_Paths()
    {
        var crd = _mlc.Transpile(typeof(EntityWithScaleSubresource));

        var subresources = crd.Spec.Versions[0].Subresources;
        subresources.Should().NotBeNull();
        subresources.Scale.Should().NotBeNull();
        subresources.Scale!.SpecReplicasPath.Should().Be(".spec.replicas");
        subresources.Scale.StatusReplicasPath.Should().Be(".status.replicas");
        subresources.Scale.LabelSelectorPath.Should().BeNull();
    }

    [Fact]
    public void Should_Add_Scale_Without_Status_SubResource()
    {
        var crd = _mlc.Transpile(typeof(EntityWithScaleSubresource));

        var subresources = crd.Spec.Versions[0].Subresources;
        subresources.Should().NotBeNull();
        subresources.Status.Should().BeNull();
    }

    [Fact]
    public void Should_Add_Scale_SubResource_With_Label_Selector_Path()
    {
        var crd = _mlc.Transpile(typeof(EntityWithScaleAndSelector));

        var subresources = crd.Spec.Versions[0].Subresources;
        subresources.Should().NotBeNull();
        subresources.Scale.Should().NotBeNull();
        subresources.Scale!.SpecReplicasPath.Should().Be(".spec.replicas");
        subresources.Scale.StatusReplicasPath.Should().Be(".status.replicas");
        subresources.Scale.LabelSelectorPath.Should().Be(".status.selector");
    }

    [Fact]
    public void Should_Add_Both_Scale_And_Status_SubResources()
    {
        var crd = _mlc.Transpile(typeof(EntityWithScaleAndStatus));

        var subresources = crd.Spec.Versions[0].Subresources;
        subresources.Should().NotBeNull();
        subresources.Scale.Should().NotBeNull();
        subresources.Scale!.SpecReplicasPath.Should().Be(".spec.replicas");
        subresources.Scale.StatusReplicasPath.Should().Be(".status.replicas");
        subresources.Status.Should().NotBeNull();
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [ScaleSubresource(".spec.replicas", ".status.replicas")]
    public sealed class EntityWithScaleSubresource : CustomKubernetesEntity<EntityWithScaleSubresource.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public int Replicas { get; set; }
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [ScaleSubresource(".spec.replicas", ".status.replicas", ".status.selector")]
    public sealed class EntityWithScaleAndSelector : CustomKubernetesEntity<EntityWithScaleAndSelector.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public int Replicas { get; set; }
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [ScaleSubresource(".spec.replicas", ".status.replicas")]
    public sealed class EntityWithScaleAndStatus
        : CustomKubernetesEntity<EntityWithScaleAndStatus.EntitySpec, EntityWithScaleAndStatus.EntityStatus>
    {
        public sealed class EntitySpec
        {
            public int Replicas { get; set; }
        }

        public sealed class EntityStatus
        {
            public int Replicas { get; set; }
        }
    }
}
