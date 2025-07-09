using KubeOps.Operator;

using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
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

using var host = builder.Build();
await host.RunAsync();
