using System.Net;

using KubeOps.Operator;
using KubeOps.Operator.Web.Builder;
using KubeOps.Operator.Web.Certificates;

var builder = WebApplication.CreateBuilder(args);
var opBuilder = builder.Services
    .AddKubernetesOperator()
//-:cnd:noEmit
#if DEBUG
    .AddCrdInstaller(c =>
    {
        // Careful, this can be very destructive.
        // c.OverwriteExisting = true;
        // c.DeleteOnShutdown = true;
    })
#endif
//+:cnd:noEmit
    .RegisterComponents();

//-:cnd:noEmit
#if DEBUG
const string ip = "192.168.1.100";
const ushort port = 443;
using var generator = new CertificateGenerator(ip);
var cert = generator.Server.CopyServerCertWithPrivateKey();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(IPAddress.Any, port, listenOptions =>
    {
        listenOptions.UseHttps(cert);
    });
});

opBuilder.UseCertificateProvider(port, ip, generator);
#endif
//+:cnd:noEmit

builder.Services
    .AddControllers();

var app = builder.Build();

app.UseRouting();
app.UseDeveloperExceptionPage();
app.MapControllers();

await app.RunAsync();
