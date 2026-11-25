// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.TestHelpers;

namespace KubeOps.KubernetesClient.Test;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class StatusPatchTest
{
    [Fact]
    public void CreateMergePatch_Returns_Null_If_The_Status_Did_Not_Change()
    {
        MergePatch(Entity(), Entity()).Should().BeNull();
    }

    [Fact]
    public void CreateMergePatch_Ignores_Changes_Outside_The_Status()
    {
        var to = Entity();
        to.Spec.Replicas = 3;
        to.Metadata.Labels = new Dictionary<string, string> { ["app"] = "demo" };
        to.Metadata.ResourceVersion = "42";

        MergePatch(Entity(), to).Should().BeNull();
    }

    [Fact]
    public void CreateMergePatch_Carries_Only_The_Changed_Fields()
    {
        var to = Entity();
        to.Status.Phase = "Ready";

        var patch = StatusPatch.CreateMergePatch(Entity().ToNode(), to.ToNode());

        patch.Should().NotBeNull();
        patch!.Type.Should().Be(V1Patch.PatchType.MergePatch);
        patch.Content.Should().Be("""{"status":{"phase":"Ready"}}""");
    }

    [Fact]
    public void CreateMergePatch_Sets_Removed_Fields_To_Null()
    {
        var to = Entity();
        to.Status.Message = null;

        MergePatch(Entity(), to).Should().Be("""{"status":{"message":null}}""");
    }

    [Fact]
    public void CreateMergePatch_Sends_A_Changed_Array_As_A_Whole()
    {
        var to = Entity();
        to.Status.Conditions.Add(new() { Type = "Available", Status = "True" });

        MergePatch(Entity(), to).Should().Be(
            """{"status":{"conditions":[{"type":"Ready","status":"True"},{"type":"Available","status":"True"}]}}""");
    }

    [Fact]
    public void CreateMergePatch_Recurses_Into_Nested_Objects()
    {
        var to = Entity();
        to.Status.Endpoint.Port = 8443;

        MergePatch(Entity(), to).Should().Be("""{"status":{"endpoint":{"port":8443}}}""");
    }

    [Fact]
    public void CreateMergePatch_Sends_The_Whole_Status_If_The_Original_Has_None()
    {
        var from = Entity();
        from.Status = null!;

        MergePatch(from, Entity()).Should().Be(
            """{"status":{"phase":"Pending","message":"Waiting","conditions":[{"type":"Ready","status":"True"}],"""
            + "\"endpoint\":{\"host\":\"demo\",\"port\":443}}}");
    }

    private static object? MergePatch(V1StatusTestEntity from, V1StatusTestEntity to) =>
        StatusPatch.CreateMergePatch(from.ToNode(), to.ToNode())?.Content;

    private static V1StatusTestEntity Entity() => new()
    {
        Metadata = new() { Name = "my-entity", NamespaceProperty = "default", ResourceVersion = "1" },
        Status = new()
        {
            Phase = "Pending",
            Message = "Waiting",
            Conditions = [new() { Type = "Ready", Status = "True" }],
            Endpoint = new() { Host = "demo", Port = 443 },
        },
    };
}
