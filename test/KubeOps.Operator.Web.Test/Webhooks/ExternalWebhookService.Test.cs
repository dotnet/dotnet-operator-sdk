// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Webhooks;
using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks;

using Microsoft.Extensions.Logging.Abstractions;

namespace KubeOps.Operator.Web.Test.Webhooks;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Webhooks)]
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

    [Fact]
    public async Task StopAsync_Removes_The_Registered_Webhook_Configurations()
    {
        var client = new CapturingKubernetesClient();
        var service = CreateService(new(TunnelUri, null), new KubeOpsWebhookConfigurationFactory(), client);
        await service.StartAsync(TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);

        client.Deleted.Should().BeEquivalentTo(
            KubeOpsWebhookConfigurationFactory.ValidatingConfigurationName,
            KubeOpsWebhookConfigurationFactory.MutatingConfigurationName);
    }

    [Fact]
    public async Task StopAsync_Keeps_Webhook_Configurations_Taken_Over_By_Another_Endpoint()
    {
        var client = new CapturingKubernetesClient();
        var service = CreateService(new(TunnelUri, null), new KubeOpsWebhookConfigurationFactory(), client);
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Another operator instance registered the same configurations against its own tunnel in the meantime.
        var otherTunnel = new Uri("https://my-tunnel-5000-other.euw.devtunnels.ms");
        await CreateService(new(otherTunnel, null), new KubeOpsWebhookConfigurationFactory(), client)
            .StartAsync(TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);

        client.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_Completes_When_The_Removal_Fails()
    {
        var client = new CapturingKubernetesClient();
        var service = CreateService(new(TunnelUri, null), new KubeOpsWebhookConfigurationFactory(), client);
        await service.StartAsync(TestContext.Current.CancellationToken);
        client.DeleteException = new HttpRequestException("The API server is not reachable.");

        var stop = () => service.StopAsync(TestContext.Current.CancellationToken);

        await stop.Should().NotThrowAsync();
    }

    private static ExternalWebhookService CreateService(
        ExternalWebhookEndpoint endpoint,
        IWebhookConfigurationFactory factory,
        CapturingKubernetesClient? client = null)
        => new(
            NullLogger<ExternalWebhookService>.Instance,
            client ?? new CapturingKubernetesClient(),
            TestWebhooks.Loader,
            endpoint,
            factory);
}
