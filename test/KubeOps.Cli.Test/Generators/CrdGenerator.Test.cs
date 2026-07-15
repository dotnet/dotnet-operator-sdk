// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Cli.Generators;

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

    private static V1CustomResourceDefinition CreateCrd() => new()
    {
        Spec = new()
        {
            Group = "testing.dev",
            Names = new() { Kind = "Widget", Plural = "widgets" },
        },
    };
}
