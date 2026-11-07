// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks;

using Microsoft.Extensions.Logging.Abstractions;

namespace KubeOps.Operator.Web.Test.Webhooks;

public sealed class ExternalWebhookServiceTest
{
    private static readonly Uri TunnelUri = new("https://my-tunnel-5000.euw.devtunnels.ms");

    [Fact]
    public async Task StartAsync_Registers_Webhooks_Against_External_Endpoint()
    {
        var factory = new CapturingWebhookConfigurationFactory();

        await CreateService(new(TunnelUri, null), factory).StartAsync(TestContext.Current.CancellationToken);

        factory.ValidatingRegistrations.Should().NotBeEmpty()
            .And.OnlyContain(r => r.Uri.Host == TunnelUri.Host && r.Uri.AbsolutePath.StartsWith("/validate/"));
        factory.MutatingRegistrations.Should().NotBeEmpty()
            .And.OnlyContain(r => r.Uri.Host == TunnelUri.Host && r.Uri.AbsolutePath.StartsWith("/mutate/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { 1, 2, 3 })]
    public async Task StartAsync_Registers_Webhooks_With_The_Configured_CaBundle(byte[]? caBundle)
    {
        var factory = new CapturingWebhookConfigurationFactory();

        await CreateService(new(TunnelUri, caBundle), factory).StartAsync(TestContext.Current.CancellationToken);

        factory.ValidatingRegistrations.Should().NotBeEmpty().And.OnlyContain(r => r.CaBundle == caBundle);
        factory.MutatingRegistrations.Should().NotBeEmpty().And.OnlyContain(r => r.CaBundle == caBundle);
    }

    private static ExternalWebhookService CreateService(
        ExternalWebhookEndpoint endpoint,
        CapturingWebhookConfigurationFactory factory)
        => new(
            NullLogger<ExternalWebhookService>.Instance,
            new CapturingKubernetesClient(),
            TestWebhooks.Loader,
            endpoint,
            factory);
}
