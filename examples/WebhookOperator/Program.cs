// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#if DEBUG
using System.Net;
#endif

using KubeOps.Operator;

#if DEBUG
using KubeOps.Operator.Web.Builder;
using KubeOps.Operator.Web.Certificates;
#endif

var builder = WebApplication.CreateBuilder(args);

#if !DEBUG
builder.Services
    .AddKubernetesOperator()
    .RegisterComponents();
#else
var opBuilder = builder.Services
    .AddKubernetesOperator()
    .RegisterComponents();
#endif

#if DEBUG
// With a tunnel (Dev Tunnels, cloudflared, ngrok) that forwards to http://localhost:5000, set
// KubeOps:Webhooks:ExternalUrl to its public https URL. Otherwise the cluster has to reach this machine
// directly and the webhooks are served via https with a generated certificate.
const string ip = "192.168.1.100";
const ushort port = 443;
using var generator = string.IsNullOrWhiteSpace(builder.Configuration["KubeOps:Webhooks:ExternalUrl"])
    ? new CertificateGenerator(ip)
    : null;

if (generator is null)
{
    opBuilder.UseExternalWebhookEndpoint();
}
else
{
    var cert = generator.Server.CopyServerCertWithPrivateKey();

    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.Listen(IPAddress.Any, port, listenOptions =>
        {
            listenOptions.UseHttps(cert);
        });
    });

    opBuilder.UseCertificateProvider(port, ip, generator);
}
#endif

builder.Services
    .AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();

await app.RunAsync();
