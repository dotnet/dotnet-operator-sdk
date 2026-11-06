// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Webhooks;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Web.Webhooks;

internal sealed class ExternalWebhookService(
    ILogger<ExternalWebhookService> logger,
    IKubernetesClient client,
    WebhookLoader loader,
    ExternalWebhookEndpoint endpoint,
    IWebhookConfigurationFactory webhookConfigurationFactory)
    : WebhookServiceBase(client, loader, endpoint.BaseUri, webhookConfigurationFactory), IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        CaBundle = endpoint.CaBundle;

        logger.LogDebug("Registering webhooks against external endpoint {Endpoint}", endpoint.BaseUri);
        await RegisterAll();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
