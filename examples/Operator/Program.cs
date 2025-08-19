// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Trace);

builder.Services
    .AddKubernetesOperator()
#if DEBUG
    .AddCrdInstaller(c =>
    {
        c.OverwriteExisting = true;
        c.DeleteOnShutdown = true;
    })
#endif
    .RegisterComponents();

using var host = builder.Build();
await host.RunAsync();
