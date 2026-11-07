// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Operator.Web.Certificates;
using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Test.Webhooks;
using KubeOps.Operator.Web.Webhooks;

using Microsoft.Extensions.Logging.Abstractions;

namespace KubeOps.Operator.Web.Test.Certificates;

[Trait("Area", "Certificate")]
public sealed class CertificateWebhookServiceTest : IDisposable
{
    private readonly CertificateGenerator _certificateProvider = new(Environment.MachineName);

    [Fact]
    public async Task StartAsync_Uses_Root_Certificate_As_Webhook_CaBundle()
    {
        var client = new CapturingKubernetesClient();
        var factory = new CapturingWebhookConfigurationFactory();
        var service = new CertificateWebhookService(
            NullLogger<CertificateWebhookService>.Instance,
            client,
            TestWebhooks.Loader,
            new(Environment.MachineName, 443),
            _certificateProvider,
            factory);

        await service.StartAsync(TestContext.Current.CancellationToken);

        var expectedRootCaBundle = _certificateProvider.Root.Certificate.EncodeToPemBytes();
        var serverCertificateBundle = _certificateProvider.Server.Certificate.EncodeToPemBytes();

        factory.ValidatingCaBundles.Should()
            .NotBeEmpty()
            .And.OnlyContain(caBundle => caBundle.SequenceEqual(expectedRootCaBundle));
        factory.MutatingCaBundles.Should()
            .NotBeEmpty()
            .And.OnlyContain(caBundle => caBundle.SequenceEqual(expectedRootCaBundle));

        factory.ValidatingCaBundles.Should()
            .NotContain(caBundle => caBundle.SequenceEqual(serverCertificateBundle));
        factory.MutatingCaBundles.Should()
            .NotContain(caBundle => caBundle.SequenceEqual(serverCertificateBundle));
    }

    [Fact]
    public async Task StopAsync_Keeps_The_Webhook_Configurations()
    {
        // The certificate provider can serve an operator inside the cluster. During a rolling update the old pod
        // stops after the new one registered the same configurations, so stopping must not remove them.
        var client = new CapturingKubernetesClient();
        var service = new CertificateWebhookService(
            NullLogger<CertificateWebhookService>.Instance,
            client,
            TestWebhooks.Loader,
            new(Environment.MachineName, 443),
            new CertificateGenerator(Environment.MachineName),
            new KubeOpsWebhookConfigurationFactory());
        await service.StartAsync(TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);

        client.Deleted.Should().BeEmpty();
        client.Existing.Should().ContainKeys(
            KubeOpsWebhookConfigurationFactory.ValidatingConfigurationName,
            KubeOpsWebhookConfigurationFactory.MutatingConfigurationName);
    }

    public void Dispose()
    {
        _certificateProvider.Dispose();
    }
}
