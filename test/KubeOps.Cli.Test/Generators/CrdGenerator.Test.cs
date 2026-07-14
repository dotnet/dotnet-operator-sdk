// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Cli.Generators;
using KubeOps.Cli.Output;

namespace KubeOps.Cli.Test.Generators;

[Trait("Area", "General")]
public sealed class CrdGeneratorTest
{
    [Fact]
    public void ConfigureConversion_WhenNoWebhookIsRegistered_ThenUsesNoneStrategy()
    {
        var crd = CreateCrd();

        CrdGenerator.ConfigureConversion(crd, false, []);

        crd.Spec.Conversion.Strategy.Should().Be("None");
        crd.Spec.Conversion.Webhook.Should().BeNull();
    }

    [Fact]
    public void ConfigureConversion_WhenWebhookIsRegistered_ThenUsesWebhookStrategy()
    {
        var crd = CreateCrd();
        byte[] caBundle = [1, 2, 3];

        CrdGenerator.ConfigureConversion(crd, true, caBundle);

        crd.Spec.Conversion.Strategy.Should().Be("Webhook");
        crd.Spec.Conversion.Webhook.ConversionReviewVersions.Should().Equal("v1");
        crd.Spec.Conversion.Webhook.ClientConfig.CaBundle.Should().Equal(caBundle);
        crd.Spec.Conversion.Webhook.ClientConfig.Service.Path.Should().Be("/convert/testing.dev/widgets");
        crd.Spec.Conversion.Webhook.ClientConfig.Service.Name.Should().Be("service");
    }

    [Fact]
    public void ResolveFileName_WhenNoNameIsConfigured_ThenUsesExistingFallback()
    {
        var result = CrdGenerator.ResolveFileName("widgets.testing.dev", OutputFormat.Yaml, []);

        result.Should().Be("widgets_testing_dev.yaml");
    }

    [Fact]
    public void ResolveFileName_WhenNameIsConfigured_ThenUsesConfiguredName()
    {
        var result = CrdGenerator.ResolveFileName(
            "widgets.testing.dev",
            OutputFormat.Yaml,
            ["widget.testing.dev.yaml"]);

        result.Should().Be("widget.testing.dev.yaml");
    }

    [Theory]
    [InlineData("")]
    [InlineData("../widget.yaml")]
    public void ResolveFileName_WhenConfiguredNameIsInvalid_ThenThrows(string fileName)
    {
        var action = () => CrdGenerator.ResolveFileName("widgets.testing.dev", OutputFormat.Yaml, [fileName]);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveFileName_WhenVersionsConfigureConflictingNames_ThenThrows()
    {
        var action = () => CrdGenerator.ResolveFileName(
            "widgets.testing.dev",
            OutputFormat.Yaml,
            ["widget-v1.yaml", "widget-v2.yaml"]);

        action.Should().Throw<InvalidOperationException>();
    }

    private static V1CustomResourceDefinition CreateCrd() => new()
    {
        Spec = new()
        {
            Group = "testing.dev",
            Names = new() { Kind = "Widget", Plural = "widgets" },
        },
    };
}
