// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Web.Test.TestApp;

/// <summary>
/// Provides a utility class for creating a test host environment for unit tests.
/// This class is designed to initialize and configure an in-memory ASP.NET Core host
/// with support for controllers and Kubernetes operator services.
/// </summary>
internal static class TestHost
{
    /// <summary>
    /// Creates and configures a test host for running the application with a test server.
    /// The host is pre-configured with necessary services and middleware required for testing
    /// Kubernetes operator functionalities in a web context.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains an instance
    /// of an IHost configured with the test server and required services.
    /// </returns>
    internal static async Task<IHost> Create()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddKubernetesOperator();
                        services.AddControllers();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();
                        });
                    });
            });

        var host = await builder.StartAsync();
        return host;
    }
}
