// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Abstractions.Test.Entities;

public class JsonPatchExtensionsTest
{
    [Fact]
    public void GetJsonDiff_Adds_Property_In_Spec()
    {
        var from = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec { Replicas = 1 },
        };
        var to = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec { Replicas = 1, RevisionHistoryLimit = 2 },
        };
        var diff = from.CreateJsonPatch(to);
        diff.ToJsonString().Should()
            .Contain("/spec/revisionHistoryLimit")
            .And.Contain("2")
            .And.Contain("add");
    }

    [Fact]
    public void GetJsonDiff_Updates_Property_In_Spec()
    {
        var from = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec { Replicas = 1 },
        };
        var to = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec { Replicas = 2 },
        };
        var diff = from.CreateJsonPatch(to);
        diff.ToJsonString().Should()
            .Contain("replace")
            .And.Contain("/spec/replicas")
            .And.Contain("2");
    }

    [Fact]
    public void GetJsonDiff_Removes_Property_In_Spec()
    {
        var from = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec { Replicas = 1, RevisionHistoryLimit = 2 },
        };
        var to = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec { Replicas = 1 },
        };
        var diff = from.CreateJsonPatch(to);
        diff.ToJsonString().Should().Contain("/spec/revisionHistoryLimit").And.Contain("remove");
    }

    [Fact]
    public void GetJsonDiff_Adds_Object_To_Containers_List()
    {
        var from = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec
            {
                Template = new V1PodTemplateSpec
                {
                    Spec = new V1PodSpec { Containers = new List<V1Container>() },
                },
            },
        };
        var to = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec
            {
                Template = new V1PodTemplateSpec
                {
                    Spec = new V1PodSpec
                    {
                        Containers = new List<V1Container>
                        {
                            new V1Container { Name = "nginx", Image = "nginx:latest" },
                        },
                    },
                },
            },
        };
        var diff = from.CreateJsonPatch(to);
        diff.ToJsonString().Should().Contain("/spec/template/spec/containers/0");
        diff.ToJsonString().Should().Contain("nginx:latest");
    }

    [Fact]
    public void GetJsonDiff_Updates_Object_In_Containers_List()
    {
        var from = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec
            {
                Template = new V1PodTemplateSpec
                {
                    Spec = new V1PodSpec
                    {
                        Containers = new List<V1Container>
                        {
                            new V1Container { Name = "nginx", Image = "nginx:1.14" },
                        },
                    },
                },
            },
        };
        var to = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec
            {
                Template = new V1PodTemplateSpec
                {
                    Spec = new V1PodSpec
                    {
                        Containers = new List<V1Container>
                        {
                            new V1Container { Name = "nginx", Image = "nginx:1.16" },
                        },
                    },
                },
            },
        };
        var diff = from.CreateJsonPatch(to);
        diff.ToJsonString().Should().Contain("replace").And.Contain("/spec/template/spec/containers/0/image");
    }

    [Fact]
    public void GetJsonDiff_Removes_Object_From_Containers_List()
    {
        var from = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec
            {
                Template = new V1PodTemplateSpec
                {
                    Spec = new V1PodSpec
                    {
                        Containers = new List<V1Container>
                        {
                            new V1Container { Name = "nginx", Image = "nginx:latest" },
                            new V1Container { Name = "nginx2", Image = "nginx:latest" },
                        },
                    },
                },
            },
        };
        var to = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = "test" },
            Spec = new V1DeploymentSpec
            {
                Template = new V1PodTemplateSpec
                {
                    Spec = new V1PodSpec
                    {
                        Containers = new List<V1Container>
                        {
                            new V1Container { Name = "nginx", Image = "nginx:latest" },
                        },
                    },
                },
            },
        };
        var diff = from.CreateJsonPatch(to);
        diff.ToJsonString().Should().Contain("/spec/template/spec/containers/1");
        diff.ToJsonString().Should().Contain("remove");
    }

    [Fact]
    public void GetJsonDiff_Filters_Metadata_Fields()
    {
        var from = new V1ConfigMap { Metadata = new V1ObjectMeta { Name = "test", ResourceVersion = "1" } }
            .Initialize();
        var to = new V1ConfigMap { Metadata = new V1ObjectMeta { Name = "test", ResourceVersion = "2" } };
        var diff = from.CreateJsonPatch(to);
        diff.Operations.Should().HaveCount(0);
    }
}
