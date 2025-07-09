using System.Net;

using KubeOps.Operator;
using KubeOps.Operator.Web.Builder;
using KubeOps.Operator.Web.Certificates;

var builder = WebApplication.CreateBuilder(args);
var opBuilder = builder.Services
    .AddKubernetesOperator()
    .RegisterComponents();

#if DEBUG
const string ip = "192.168.1.100";
const ushort port = 443;
using var generator = new CertificateGenerator(ip);
var cert = generator.Server.CopyServerCertWithPrivateKey();

// Configure Kestrel to listen on IPv4, use port 443, and use the server certificate
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(IPAddress.Any, port, listenOptions =>
    {
        listenOptions.UseHttps(cert);
    });
});

opBuilder.UseCertificateProvider(port, ip, generator);
#endif

builder.Services
    .AddControllers();

var app = builder.Build();

app.UseRouting();
app.UseDeveloperExceptionPage();
app.MapControllers();

await app.RunAsync();
