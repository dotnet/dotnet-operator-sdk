// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.KubernetesClient;
using KubeOps.Operator.Web.Certificates;
using KubeOps.Operator.Web.Webhooks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Web.LocalTunnel;

internal class TunnelWebhookService(
    ILogger<CertificateWebhookService> logger,
    IKubernetesClient client,
    WebhookLoader loader,
    WebhookConfig config,
    DevelopmentTunnel developmentTunnel)
    : WebhookServiceBase(client, loader, config), IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Uri = await developmentTunnel.StartAsync(cancellationToken);

        logger.LogDebug("Registering webhooks");
        await RegisterAll();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        developmentTunnel.Dispose();
        return Task.CompletedTask;
    }
}
