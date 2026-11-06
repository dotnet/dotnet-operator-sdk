// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Certificates;
using KubeOps.Abstractions.Webhooks;
using KubeOps.Operator.Web.Certificates;
using KubeOps.Operator.Web.Webhooks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KubeOps.Operator.Web.Builder;

/// <summary>
/// Method extensions for the operator builder to register web specific services.
/// </summary>
public static class OperatorBuilderExtensions
{
    /// <summary>
    /// Adds a hosted service to the system that uses the server certificate from an <see cref="ICertificateProvider"/>
    /// implementation to configure development webhooks. The webhooks will be configured to use the hostname and port.
    /// </summary>
    /// <param name="builder">The operator builder.</param>
    /// <param name="port">The port that the webhooks will use to connect to the operator.</param>
    /// <param name="hostname">The hostname, IP, or FQDN of the machine running the operator.</param>
    /// <param name="certificateProvider">The <see cref="ICertificateProvider"/> the <see cref="CertificateWebhookService"/>
    /// will use to generate the PEM-encoded server certificate for the webhooks.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// Use the development webhooks.
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// string ip = "192.168.1.100";
    /// ushort port = 443;
    ///
    /// using CertificateGenerator generator = new CertificateGenerator(ip);
    /// using X509Certificate2 cert = generator.Server.CopyServerCertWithPrivateKey();
    /// // Configure Kestrel to listen on IPv4, use port 443, and use the server certificate
    /// builder.WebHost.ConfigureKestrel(serverOptions =>
    /// {
    ///     serverOptions.Listen(System.Net.IPAddress.Any, port, async listenOptions =>
    ///     {
    ///         listenOptions.UseHttps(cert);
    ///     });
    /// });
    ///  builder.Services
    ///      .AddKubernetesOperator()
    ///      // Create the development webhook service using the cert provider
    ///      .UseCertificateProvider(port, ip, generator)
    ///      // More code
    ///
    /// </code>
    /// </example>
    public static IOperatorBuilder UseCertificateProvider(this IOperatorBuilder builder, ushort port, string hostname, ICertificateProvider certificateProvider)
    {
        builder.Services.AddHostedService<CertificateWebhookService>();
        builder.Services.AddSingleton(new WebhookLoader(Assembly.GetEntryAssembly()!));
        builder.Services.AddSingleton(new WebhookConfig(hostname, port));
        builder.Services.AddSingleton(certificateProvider);
        builder.Services.TryAddSingleton<IWebhookConfigurationFactory, KubeOpsWebhookConfigurationFactory>();

        return builder;
    }

    /// <summary>
    /// Adds a hosted service that registers the webhooks against an externally provided HTTPS endpoint,
    /// for example the public URL of a development tunnel (Microsoft Dev Tunnels, cloudflared, ngrok)
    /// that forwards to the locally running operator.
    /// </summary>
    /// <remarks>
    /// The endpoint terminates TLS, so the operator itself can listen on plain HTTP. Without a
    /// <paramref name="caBundle"/>, the Kubernetes API server validates the endpoint's certificate against its
    /// system trust roots, which is what publicly trusted tunnel certificates need.
    /// </remarks>
    /// <param name="builder">The operator builder.</param>
    /// <param name="baseUri">
    /// The absolute HTTPS base URL that forwards to the operator. The webhook routes are appended to it.
    /// </param>
    /// <param name="caBundle">
    /// Optional PEM-encoded CA bundle the API server uses to validate the endpoint's certificate.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentException">The URL is not an absolute HTTPS URL without query and fragment.</exception>
    /// <example>
    /// Register the webhooks against a dev tunnel in debug builds.
    /// <code>
    /// builder.Services
    ///     .AddKubernetesOperator()
    /// #if DEBUG
    ///     .UseExternalWebhookEndpoint(new Uri("https://my-tunnel-5000.euw.devtunnels.ms"))
    /// #endif
    ///     .RegisterComponents();
    /// </code>
    /// </example>
    public static IOperatorBuilder UseExternalWebhookEndpoint(
        this IOperatorBuilder builder,
        Uri baseUri,
        byte[]? caBundle = null)
    {
        var endpoint = ExternalWebhookEndpoint.Create(baseUri, caBundle);
        builder.Services.AddSingleton(endpoint);
        return builder.AddExternalWebhookService();
    }

    /// <summary>
    /// Adds a hosted service that registers the webhooks against an externally provided HTTPS endpoint,
    /// read from the configuration key <c>KubeOps:Webhooks:ExternalUrl</c>
    /// (environment variable <c>KubeOps__Webhooks__ExternalUrl</c>).
    /// </summary>
    /// <remarks>
    /// The Aspire hosting integration sets this key when the operator is started with
    /// <c>WithWebhookDevTunnel()</c>. The value is read when the operator starts; a missing or invalid value
    /// fails the start. See <see cref="UseExternalWebhookEndpoint(IOperatorBuilder, Uri, byte[])"/> for details.
    /// </remarks>
    /// <param name="builder">The operator builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IOperatorBuilder UseExternalWebhookEndpoint(this IOperatorBuilder builder)
    {
        builder.Services.AddSingleton(services =>
            ExternalWebhookEndpoint.FromConfiguration(services.GetRequiredService<IConfiguration>()));
        return builder.AddExternalWebhookService();
    }

    private static IOperatorBuilder AddExternalWebhookService(this IOperatorBuilder builder)
    {
        builder.Services.AddHostedService<ExternalWebhookService>();
        builder.Services.AddSingleton(new WebhookLoader(Assembly.GetEntryAssembly()!));
        builder.Services.TryAddSingleton<IWebhookConfigurationFactory, KubeOpsWebhookConfigurationFactory>();

        return builder;
    }
}
