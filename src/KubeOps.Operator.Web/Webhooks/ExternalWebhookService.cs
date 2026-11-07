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
    : WebhookServiceBase(client, loader, endpoint.BaseUri, endpoint.CaBundle, webhookConfigurationFactory),
        IHostedService
{
    private static readonly TimeSpan UnregisterTimeout = TimeSpan.FromSeconds(5);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Registering webhooks against external endpoint {Endpoint}", endpoint.BaseUri);
        await RegisterAll();
    }

    /// <summary>
    /// Removes the webhook configurations, so they do not reject requests while the operator and its tunnel are
    /// stopped. This is best effort: it is skipped after <see cref="UnregisterTimeout"/>, and nothing is removed
    /// when the process ends without stopping the host.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown should no longer be graceful.</param>
    /// <returns>A task that completes when the configurations were removed or the attempt failed.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(UnregisterTimeout);

        try
        {
            logger.LogDebug("Removing webhook configurations of external endpoint {Endpoint}", endpoint.BaseUri);
            await UnregisterAll(timeout.Token);
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not remove the webhook configurations of external endpoint {Endpoint}. They reject requests " +
                "for the webhook resources until the operator runs again or they are deleted.",
                endpoint.BaseUri);
        }
    }
}
