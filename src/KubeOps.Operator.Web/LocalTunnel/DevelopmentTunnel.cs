// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks;

using Localtunnel;
using Localtunnel.Endpoints.Http;
using Localtunnel.Handlers.Kestrel;
using Localtunnel.Processors;
using Localtunnel.Tunnels;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Web.LocalTunnel;

internal class DevelopmentTunnel(ILoggerFactory loggerFactory, WebhookConfig config) : IDisposable
{
    private readonly LocaltunnelClient _tunnelClient = new(loggerFactory);
    private Tunnel? _tunnel;

    public async Task<Uri> StartAsync(CancellationToken cancellationToken)
    {
        _tunnel = await _tunnelClient.OpenAsync(
            new KestrelTunnelConnectionHandler(
                new HttpRequestProcessingPipelineBuilder()
                    .Append(new HttpHostHeaderRewritingRequestProcessor(config.Hostname)).Build(),
                new HttpTunnelEndpointFactory(config.Hostname, config.Port)),
            cancellationToken: cancellationToken);
        await _tunnel.StartAsync(cancellationToken: cancellationToken);
        return _tunnel.Information.Url;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _tunnel?.Dispose();
    }
}
