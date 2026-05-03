// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Crds;

namespace KubeOps.Abstractions.Test.Crds;

public sealed class CrdInstallerSettingsBuilderTest
{
    [Fact]
    public void Build_Produces_Correct_Default_Values()
    {
        var settings = new CrdInstallerSettingsBuilder().Build();

        settings.OverwriteExisting.Should().BeFalse();
        settings.DeleteOnShutdown.Should().BeFalse();
    }

    [Fact]
    public void Builder_Accepts_All_Property_Setters_And_Passes_Them_Through_Build()
    {
        var settings = new CrdInstallerSettingsBuilder
        {
            OverwriteExisting = true,
            DeleteOnShutdown = true,
        }.Build();

        settings.OverwriteExisting.Should().BeTrue();
        settings.DeleteOnShutdown.Should().BeTrue();
    }

    [Fact]
    public void Fluent_Api_Sets_All_Properties_And_Builds_Correctly()
    {
        var settings = new CrdInstallerSettingsBuilder()
            .WithOverwriteExisting()
            .WithDeleteOnShutdown()
            .Build();

        settings.OverwriteExisting.Should().BeTrue();
        settings.DeleteOnShutdown.Should().BeTrue();
    }

    [Fact]
    public void Fluent_Api_Can_Disable_All_Properties_Explicitly()
    {
        var settings = new CrdInstallerSettingsBuilder
        {
            OverwriteExisting = true,
            DeleteOnShutdown = true,
        }
            .WithOverwriteExisting(false)
            .WithDeleteOnShutdown(false)
            .Build();

        settings.OverwriteExisting.Should().BeFalse();
        settings.DeleteOnShutdown.Should().BeFalse();
    }
}
