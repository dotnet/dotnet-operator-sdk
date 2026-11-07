// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Runtime.InteropServices;

using k8s;
using k8s.Models;

using KubeOps.KubernetesClient;
using KubeOps.Operator.Web.Builder;
using KubeOps.Operator.Web.Certificates;
using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks;
using KubeOps.Transpiler;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Web.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public class IntegrationTestCollection : ICollectionFixture<CrdInstaller>, ICollectionFixture<ApplicationProvider>
{
    public const string Name = "Integration Tests";
}

[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public abstract class IntegrationTestBase;

public sealed class CrdInstaller : IAsyncLifetime
{
    private List<V1CustomResourceDefinition> _crds = [];

    public async ValueTask InitializeAsync()
    {
        await using var p = new MlcProvider();
        await p.InitializeAsync();
        _crds = p.Mlc.Transpile(new[] { typeof(V1OperatorWebIntegrationTestEntity) }).ToList();

        using var client = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
        foreach (var crd in _crds)
        {
            switch (await client.ApiextensionsV1.ListCustomResourceDefinitionAsync(
                        fieldSelector: $"metadata.name={crd.Name()}"))
            {
                case { Items: [var existing] }:
                    crd.Metadata.ResourceVersion = existing.ResourceVersion();
                    await client.ApiextensionsV1.ReplaceCustomResourceDefinitionAsync(crd, crd.Name());
                    break;
                default:
                    await client.ApiextensionsV1.CreateCustomResourceDefinitionAsync(crd);
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var client = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
        foreach (var crd in _crds)
        {
            await client.ApiextensionsV1.DeleteCustomResourceDefinitionAsync(crd.Name());
        }
    }
}

public sealed class MlcProvider : IAsyncLifetime
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    static MlcProvider()
    {
        MSBuildLocator.RegisterDefaults();
    }

    public MetadataLoadContext Mlc { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var assemblyConfigurationAttribute =
            typeof(MlcProvider).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
        var buildConfigurationName = assemblyConfigurationAttribute?.Configuration ?? "Debug";

        try
        {
            await Semaphore.WaitAsync();
            using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
            {
                { "Configuration", buildConfigurationName },
            });
            workspace.SkipUnrecognizedProjects = true;
            workspace.LoadMetadataForReferencedProjects = true;
            var project = await workspace.OpenProjectAsync("../../../KubeOps.Operator.Web.Test.csproj");

            Mlc = ContextCreator.Create(Directory
                .GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
                .Concat(Directory.GetFiles(Path.GetDirectoryName(project.OutputFilePath)!, "*.dll"))
                .Distinct(), coreAssemblyName: typeof(object).Assembly.GetName().Name);
        }
        finally
        {
            Semaphore.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        Mlc.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Admission webhook integration tests need the Kubernetes API server to call the test app.
/// They only run when <c>KUBEOPS_TEST_WEBHOOK_HOST</c> holds a host name or IP address under which the
/// API server reaches this machine, for example the gateway of the <c>kind</c> Docker network or
/// <c>host.docker.internal</c>. The test app then serves HTTPS on <see cref="Port"/> with a generated
/// certificate and registers the webhooks with its CA bundle.
/// </summary>
public static class ExternalWebhookTestEndpoint
{
    public const string HostVariable = "KUBEOPS_TEST_WEBHOOK_HOST";

    public const int Port = 5443;

    public const string SkipReason =
        $"Requires the API server to reach the test app; set {HostVariable} to a host name or IP of this machine.";

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    public static Uri BaseUri => new UriBuilder(Uri.UriSchemeHttps, Host, Port).Uri;

    private static string? Host => Environment.GetEnvironmentVariable(HostVariable)?.Trim();
}

public sealed class ApplicationProvider : IAsyncLifetime
{
    private CertificateGenerator? _certificates;
    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        if (!ExternalWebhookTestEndpoint.IsConfigured)
        {
            return;
        }

        var baseUri = ExternalWebhookTestEndpoint.BaseUri;
        _certificates = new CertificateGenerator(baseUri.Host);
        var serverCertificate = _certificates.Server.CopyServerCertWithPrivateKey();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(c =>
            c.ListenAnyIP(baseUri.Port, listen => listen.UseHttps(serverCertificate)));
        builder.Services
            .AddKubernetesOperator()
            .UseExternalWebhookEndpoint(baseUri, _certificates.Root.Certificate.EncodeToPemBytes());

        builder.Services.AddControllers().AddApplicationPart(typeof(ApplicationProvider).Assembly);
        builder.Services.AddHealthChecks();

        builder.Services.AddLogging(c =>
        {
            c.AddSimpleConsole();
#if DEBUG
            c.SetMinimumLevel(LogLevel.Trace);
#else
                c.SetMinimumLevel(LogLevel.None);
#endif
        });

        builder.Services.RemoveAll<WebhookLoader>();
        builder.Services.AddSingleton(TestWebhooks.Loader);

        _app = builder.Build();

        _app.UseRouting();
        _app.UseDeveloperExceptionPage();
        _app.MapControllers();

        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null)
        {
            return;
        }

        await _app.DisposeAsync();
        _certificates?.Dispose();

        // The webhook configurations fail closed; remove them so they do not outlive the test app.
        using var client = new KubernetesClient.KubernetesClient() as IKubernetesClient;
        await client.DeleteAsync<V1ValidatingWebhookConfiguration>(
            KubeOpsWebhookConfigurationFactory.ValidatingConfigurationName);
        await client.DeleteAsync<V1MutatingWebhookConfiguration>(
            KubeOpsWebhookConfigurationFactory.MutatingConfigurationName);
    }
}
