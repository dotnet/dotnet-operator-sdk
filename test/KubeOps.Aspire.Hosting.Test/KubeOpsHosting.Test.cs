// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using FluentAssertions;

namespace KubeOps.Aspire.Hosting.Test;

public class KubeOpsHostingTest
{
    [Fact]
    public void AddKubeOps_Adds_Named_Project_Resource()
    {
        var builder = DistributedApplication.CreateBuilder([]);

        builder.AddKubeOps<TestProjectMetadata>("operator");

        builder.Resources.OfType<ProjectResource>()
            .Should().ContainSingle(resource => resource.Name == "operator");
    }

    [Fact]
    public void AddKubeOps_Returns_Builder_For_Chaining()
    {
        var builder = DistributedApplication.CreateBuilder([]);

        var resourceBuilder = builder.AddKubeOps<TestProjectMetadata>("operator");

        resourceBuilder.Should().NotBeNull();
        resourceBuilder.Resource.Name.Should().Be("operator");
    }

    private sealed class TestProjectMetadata : IProjectMetadata
    {
        public string ProjectPath => typeof(TestProjectMetadata).Assembly.Location;
    }
}
