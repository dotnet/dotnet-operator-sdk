// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Operator.Web.Webhooks;

using Microsoft.Extensions.Configuration;

namespace KubeOps.Operator.Web.Test.Webhooks;

public sealed class ExternalWebhookEndpointTest
{
    [Fact]
    public void FromConfiguration_Should_Fail_When_Url_Is_Missing()
    {
        var act = () => ExternalWebhookEndpoint.FromConfiguration(Configuration(null));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{ExternalWebhookEndpoint.ConfigurationKey}*");
    }

    [Theory]
    [InlineData("webhooks")]
    [InlineData("/webhooks")]
    [InlineData("http://localhost:5000")]
    [InlineData("https://my-tunnel.example.com/?token=abc")]
    public void FromConfiguration_Should_Fail_When_Url_Is_Invalid(string url)
    {
        var act = () => ExternalWebhookEndpoint.FromConfiguration(Configuration(url));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{ExternalWebhookEndpoint.ConfigurationKey}*");
    }

    [Fact]
    public void FromConfiguration_Should_Accept_Aspire_Endpoint_Format()
    {
        var endpoint = ExternalWebhookEndpoint.FromConfiguration(
            Configuration("https://operator-webhooks-1a2b3c4d-5000.euw.devtunnels.ms:443"));

        endpoint.BaseUri.Host.Should().Be("operator-webhooks-1a2b3c4d-5000.euw.devtunnels.ms");
        endpoint.BaseUri.Port.Should().Be(443);
    }

    private static IConfiguration Configuration(string? url)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ExternalWebhookEndpoint.ConfigurationKey] = url })
            .Build();
}
