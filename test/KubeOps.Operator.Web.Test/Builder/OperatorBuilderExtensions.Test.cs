// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.Versioning;

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Certificates;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Web.Builder;
using KubeOps.Operator.Web.Certificates;
using KubeOps.Operator.Web.LocalTunnel;
using KubeOps.Operator.Web.Webhooks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Web.Test.Builder;
[RequiresPreviewFeatures]
public class OperatorBuilderExtensionsTest : IDisposable
{
    private readonly IOperatorBuilder _builder = new OperatorBuilder(new ServiceCollection(), new());
    private readonly CertificateGenerator _certProvider = new(Environment.MachineName);

    [Fact]
    public void Should_Add_Development_Tunnel()
    {
        _builder.AddDevelopmentTunnel(4242);

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(TunnelWebhookService) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Should_Add_WebhookConfig()
    {
        _builder.AddDevelopmentTunnel(1337, "my-host");
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(WebhookConfig) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

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
    public void Should_Add_Certificate_Provider()
    {
        _builder.UseCertificateProvider(54321, Environment.MachineName, _certProvider);

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(ICertificateProvider) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    public void Dispose()
    {
        _certProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
