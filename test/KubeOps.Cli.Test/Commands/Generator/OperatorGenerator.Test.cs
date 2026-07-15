// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Cli.Commands.Generator;

namespace KubeOps.Cli.Test.Commands.Generator;

[Trait("Area", "General")]
public sealed class OperatorGeneratorTest
{
    [Fact]
    public void Command_WhenResourcesAreOmitted_ThenSelectsAllResources()
    {
        var result = OperatorGenerator.Command.Parse(
            ["operator", "test-operator", "src/KubeOps.Cli/KubeOps.Cli.csproj"]);

        result.Errors.Should().BeEmpty();
        result.GetValue(Options.OperatorResources).Should().Equal(OperatorResource.All);
    }

    [Fact]
    public void Command_WhenResourcesAreSpecified_ThenSelectsOnlySpecifiedResources()
    {
        var result = OperatorGenerator.Command.Parse(
            ["operator", "test-operator", "src/KubeOps.Cli/KubeOps.Cli.csproj", "--resources", "crds", "rbac"]);

        result.Errors.Should().BeEmpty();
        result.GetValue(Options.OperatorResources).Should().Equal(OperatorResource.Crds, OperatorResource.Rbac);
    }

    [Fact]
    public void BuildTarget_WhenResourcesAreConfigured_ThenPlacesThemAfterPositionalArguments()
    {
        var targetPath = Path.GetFullPath(
            "../../../../../src/KubeOps.Operator/Build/KubeOps.Operator.targets",
            AppContext.BaseDirectory);
        var target = File.ReadAllText(targetPath);

        target.IndexOf("$(OperatorName) $(MSBuildProjectFullPath)", StringComparison.Ordinal).Should()
            .BeLessThan(target.IndexOf("$(KubeOpsResourcesOption)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, false, false, false, false, true)]
    [InlineData(false, true, false, true, false, true)]
    [InlineData(false, false, true, false, true, true)]
    [InlineData(false, false, true, true, false, false)]
    [InlineData(false, true, false, false, true, false)]
    public void RequiresCertificates_WhenResourcesAndWebhookTypesAreSpecified_ThenUsesActualDependencies(
        bool generatesCertificates,
        bool generatesWebhookConfigurations,
        bool generatesCrds,
        bool hasAdmissionWebhooks,
        bool hasConversionWebhooks,
        bool expected)
    {
        var result = OperatorGenerator.RequiresCertificates(
            generatesCertificates,
            generatesWebhookConfigurations,
            generatesCrds,
            hasAdmissionWebhooks,
            hasConversionWebhooks);

        result.Should().Be(expected);
    }
}
