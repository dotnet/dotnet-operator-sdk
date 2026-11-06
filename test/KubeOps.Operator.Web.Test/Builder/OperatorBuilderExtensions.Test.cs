// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Certificates;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Web.Builder;
using KubeOps.Operator.Web.Certificates;
using KubeOps.Operator.Web.Webhooks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Web.Test.Builder;

public sealed class OperatorBuilderExtensionsTest : IDisposable
{
    private const string TunnelUrl = "https://my-tunnel-5000.euw.devtunnels.ms";

    private readonly OperatorBuilder _builder = new(new ServiceCollection(), new OperatorSettingsBuilder().Build());
    private readonly CertificateGenerator _certProvider = new(Environment.MachineName);

    [Fact]
    public void Should_Add_Webhook_Service()
    {
        _builder.UseCertificateProvider(12345, Environment.MachineName, _certProvider);

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(CertificateWebhookService) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Should_Add_WebhookConfig()
    {
        _builder.UseCertificateProvider(1337, "my-host", _certProvider);

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(WebhookConfig) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Should_Add_Certificate_Provider()
    {
        _builder.UseCertificateProvider(54321, Environment.MachineName, _certProvider);

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(ICertificateProvider) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Should_Add_External_Webhook_Service()
    {
        _builder.UseExternalWebhookEndpoint(new Uri(TunnelUrl));

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(ExternalWebhookService) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Should_Register_External_Endpoint_With_CaBundle()
    {
        byte[] caBundle = [1, 2, 3];

        _builder.UseExternalWebhookEndpoint(new Uri(TunnelUrl), caBundle);

        var endpoint = _builder.Services.BuildServiceProvider().GetRequiredService<ExternalWebhookEndpoint>();
        endpoint.BaseUri.Should().Be(new Uri(TunnelUrl));
        endpoint.CaBundle.Should().Equal(caBundle);
    }

    [Fact]
    public void Should_Read_External_Endpoint_From_Configuration()
    {
        _builder.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ExternalWebhookEndpoint.ConfigurationKey] = TunnelUrl })
            .Build());

        _builder.UseExternalWebhookEndpoint();

        var endpoint = _builder.Services.BuildServiceProvider().GetRequiredService<ExternalWebhookEndpoint>();
        endpoint.BaseUri.Should().Be(new Uri(TunnelUrl));
        endpoint.CaBundle.Should().BeNull();
    }

    [Theory]
    [InlineData("http://my-tunnel.example.com")]
    [InlineData("https://my-tunnel.example.com/?token=abc")]
    [InlineData("https://my-tunnel.example.com/#fragment")]
    public void Should_Reject_Invalid_External_Endpoint(string url)
    {
        var act = () => _builder.UseExternalWebhookEndpoint(new Uri(url));

        act.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        _certProvider.Dispose();
    }
}
