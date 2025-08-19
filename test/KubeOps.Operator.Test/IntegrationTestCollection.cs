// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Transpiler;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public class IntegrationTestCollection : ICollectionFixture<CrdInstaller>
{
    public const string Name = "Integration Tests";
}

[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private IHost? _host;

    protected IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException();

    public virtual async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
#if DEBUG
        builder.Logging.AddSystemdConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
#else
        builder.Logging.SetMinimumLevel(LogLevel.None);
#endif
        ConfigureHost(builder);
        _host = builder.Build();
        await _host.StartAsync();
    }

    public virtual async Task DisposeAsync()
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync();
    }

    protected abstract void ConfigureHost(HostApplicationBuilder builder);
}

public sealed class TestNamespaceProvider : IAsyncLifetime
{
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private V1Namespace _namespace = null!;

    public string Namespace { get; } = Guid.NewGuid().ToString().ToLower();

    public async Task InitializeAsync()
    {
        _namespace =
            await _client.CreateAsync(new V1Namespace(metadata: new V1ObjectMeta(name: Namespace)).Initialize());
    }

    public async Task DisposeAsync()
    {
        await _client.DeleteAsync(_namespace);
        _client.Dispose();
    }
}

public sealed class CrdInstaller : IAsyncLifetime
{
    private List<V1CustomResourceDefinition> _crds = [];

    public async Task InitializeAsync()
    {
        await using var p = new MlcProvider();
        await p.InitializeAsync();
        _crds = p.Mlc.Transpile(new[] { typeof(V1OperatorIntegrationTestEntity) }).ToList();

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

    public async Task DisposeAsync()
    {
        using var client = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
        foreach (var crd in _crds)
        {
            await client.ApiextensionsV1.DeleteCustomResourceDefinitionAsync(crd.Name());
        }
    }
}
