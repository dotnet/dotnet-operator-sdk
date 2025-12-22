// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using Json.Patch;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Abstractions.Test.Entities;

#pragma warning disable CA2252 // Opt in to preview features before using them

public sealed class JsonPatchExtensionsTest
{
    [Fact]
    public void GetJsonDiff_Adds_Property_In_Spec()
    {
        var from = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new() { Replicas = 1 },
        };
        var to = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new() { Replicas = 1, RevisionHistoryLimit = 2 },
        };
        var diff = from.CreateJsonPatch(to);

        diff.Operations.Should().HaveCount(1);
        diff.Operations[0].Op.Should().Be(OperationType.Add);
        diff.Operations[0].Path.ToString().Should().Be("/spec/revisionHistoryLimit");
        diff.Operations[0].Value!.AsValue().Should().BeEquivalentTo(2);
    }

    [Fact]
    public void GetJsonDiff_Updates_Property_In_Spec()
    {
        var from = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new() { Replicas = 1 },
        };
        var to = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new() { Replicas = 2 },
        };
        var diff = from.CreateJsonPatch(to);

        diff.Operations.Should().HaveCount(1);
        diff.Operations[0].Op.Should().Be(OperationType.Replace);
        diff.Operations[0].Path.ToString().Should().Be("/spec/replicas");
        diff.Operations[0].Value!.AsValue().Should().BeEquivalentTo(2);
    }

    [Fact]
    public void GetJsonDiff_Removes_Property_In_Spec()
    {
        var from = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new() { Replicas = 1, RevisionHistoryLimit = 2 },
        };
        var to = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new() { Replicas = 1 },
        };
        var diff = from.CreateJsonPatch(to);

        diff.Operations.Should().HaveCount(1);
        diff.Operations[0].Op.Should().Be(OperationType.Remove);
        diff.Operations[0].Path.ToString().Should().Be("/spec/revisionHistoryLimit");
        diff.Operations[0].Value.Should().BeNull();
    }

    [Fact]
    public void GetJsonDiff_Adds_Object_To_Containers_List()
    {
        var from = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new()
            {
                Template = new()
                {
                    Spec = new() { Containers = new List<V1Container>() },
                },
            },
        };
        var to = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new()
            {
                Template = new()
                {
                    Spec = new()
                    {
                        Containers = new List<V1Container>
                        {
                            new() { Name = "nginx", Image = "nginx:latest" },
                        },
                    },
                },
            },
        };

        var diff = from.CreateJsonPatch(to);
        diff.Operations.Should().HaveCount(1);
        diff.Operations[0].Op.Should().Be(OperationType.Replace);
        diff.Operations[0].Path.ToString().Should().Be("/spec/template/spec/containers");
        diff.Operations[0].Value!.AsArray().Should().HaveCount(1);
        diff.Operations[0].Value!.AsArray()[0].Should().HaveProperty("image").Which.ToString().Should().Be("nginx:latest");
        diff.Operations[0].Value!.AsArray()[0].Should().HaveProperty("name").Which.ToString().Should().Be("nginx");
    }

    [Fact]
    public void GetJsonDiff_Updates_Object_In_Containers_List()
    {
        var from = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new()
            {
                Template = new()
                {
                    Spec = new()
                    {
                        Containers = new List<V1Container>
                        {
                            new() { Name = "nginx", Image = "nginx:1.14" },
                        },
                    },
                },
            },
        };
        var to = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new()
            {
                Template = new()
                {
                    Spec = new()
                    {
                        Containers = new List<V1Container>
                        {
                            new() { Name = "nginx", Image = "nginx:1.16" },
                        },
                    },
                },
            },
        };
        var diff = from.CreateJsonPatch(to);

        diff.Operations.Should().HaveCount(1);
        diff.Operations[0].Op.Should().Be(OperationType.Replace);
        diff.Operations[0].Path.ToString().Should().Be("/spec/template/spec/containers/0/image");
        diff.Operations[0].Value!.AsValue().Should().BeEquivalentTo("nginx:1.16");
    }

    [Fact]
    public void GetJsonDiff_Removes_Object_From_Containers_List()
    {
        var from = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new()
            {
                Template = new()
                {
                    Spec = new()
                    {
                        Containers = new List<V1Container>
                        {
                            new() { Name = "nginx", Image = "nginx:latest" },
                            new() { Name = "nginx2", Image = "nginx:latest" },
                        },
                    },
                },
            },
        };
        var to = new V1Deployment
        {
            Metadata = new() { Name = "test" },
            Spec = new()
            {
                Template = new()
                {
                    Spec = new()
                    {
                        Containers = new List<V1Container>
                        {
                            new() { Name = "nginx", Image = "nginx:latest" },
                        },
                    },
                },
            },
        };
        var diff = from.CreateJsonPatch(to);

        diff.Operations.Should().HaveCount(1);
        diff.Operations[0].Op.Should().Be(OperationType.Remove);
        diff.Operations[0].Path.ToString().Should().Be("/spec/template/spec/containers/1");
        diff.Operations[0].Value.Should().BeNull();
    }

    [Fact]
    public void GetJsonDiff_Filters_Metadata_Fields()
    {
        var from = new V1ConfigMap
            {
                Metadata = new()
                {
                    Name = "test",
                    ResourceVersion = "1",
                },
            }
            .Initialize();
        var to = new V1ConfigMap
        {
            Metadata = new()
            {
                Name = "test",
                ResourceVersion = "2",
            },
        };
        var diff = from.CreateJsonPatch(to);

        diff.Operations.Should().HaveCount(0);
    }
}
