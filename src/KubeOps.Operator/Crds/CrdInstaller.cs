// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Crds;
using KubeOps.Abstractions.Entities.Attributes;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Retry;
using KubeOps.Transpiler;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Crds;

internal sealed class CrdInstaller : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly IKubernetesClient _client;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<uint, TimeSpan> _retryDelayFactory;
    private readonly ILogger<CrdInstaller> _logger;
    private readonly CrdInstallerSettings _settings;
    private List<V1CustomResourceDefinition> _crds = [];
    private bool _disposed;
    private Task? _installationTask;

    public CrdInstaller(ILogger<CrdInstaller> logger, CrdInstallerSettings settings, IKubernetesClient client)
        : this(
            logger,
            settings,
            client,
            ExponentialRetryBackoff.GetDelayWithJitter)
    {
    }

    internal CrdInstaller(
        ILogger<CrdInstaller> logger,
        CrdInstallerSettings settings,
        IKubernetesClient client,
        Func<uint, TimeSpan> retryDelayFactory)
    {
        _logger = logger;
        _settings = settings;
        _client = client;
        _retryDelayFactory = retryDelayFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _installationTask = Task.Run(RunInstallerWithRetriesAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _cts.CancelAsync();

        if (_installationTask is not null)
        {
            await _installationTask.WaitAsync(cancellationToken);
        }

        if (!_settings.DeleteOnShutdown)
        {
            _logger.LogDebug("Skipping CRD deletion on shutdown as per settings.");
            return;
        }

        _logger.LogInformation("Deleting CRDs on shutdown.");
        foreach (var crd in _crds)
        {
            try
            {
                _logger.LogInformation("Deleting CRD {Name}.", crd.Name());
                await _client.DeleteAsync(crd, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete CRD {Name}.", crd.Name());
            }
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await CastAndDispose(_cts);
        _disposed = true;

        static async ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
            {
                await resourceAsyncDisposable.DisposeAsync();
            }
            else
            {
                resource.Dispose();
            }
        }
    }

    private async Task RunInstallerWithRetriesAsync()
    {
        uint installRetries = 0;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await InstallAsync(_cts.Token);
                return;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                installRetries++;
                var delay = _retryDelayFactory(installRetries);

                _logger.LogError(
                    exception,
                    "Failed to install CRDs. Wait {Seconds}s before attempting to install them again.",
                    delay.TotalSeconds);
                try
                {
                    await Task.Delay(delay, _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to install CRDs due to a non-transient error.");
                return;
            }
        }

        return;

        static bool IsTransient(Exception exception)
        {
            return exception switch
            {
                HttpRequestException or TimeoutException or TaskCanceledException => true,
                KubernetesException { Status.Code: null } => true,
                KubernetesException { Status.Code: (int)HttpStatusCode.RequestTimeout } => true,
                KubernetesException { Status.Code: (int)HttpStatusCode.Conflict } => true,
                KubernetesException { Status.Code: (int)HttpStatusCode.TooManyRequests } => true,
                KubernetesException { Status.Code: >= 500 } => true,
                _ => false,
            };
        }
    }

    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Execute CRD installer with overwrite: {Overwrite}", _settings.OverwriteExisting);
        var assembly = Assembly.GetEntryAssembly();
        if (assembly is null)
        {
            _logger.LogError("No entry assembly found, cannot install CRDs.");
            return;
        }

        var entities = assembly
            .DefinedTypes
            .Where(t => t is { IsInterface: false, IsAbstract: false, IsGenericType: false })
            .Select(t => (t, attrs: CustomAttributeData.GetCustomAttributes(t)))
            .Where(e => e.attrs.Any(a => a.AttributeType.Name == nameof(KubernetesEntityAttribute)) &&
                        e.attrs.All(a => a.AttributeType.Name != nameof(IgnoreAttribute)))
            .Select(e => e.t);

        using var mlc = ContextCreator.Create(
            Directory
                .GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
                .Concat(
                    Directory.GetFiles(Path.GetDirectoryName(assembly.Location)!, "*.dll"))
                .Distinct(),
            coreAssemblyName: typeof(object).Assembly.GetName().Name);
        _crds = mlc.Transpile(entities).ToList();

        foreach (var crd in _crds)
        {
            var existing =
                await _client.GetAsync<V1CustomResourceDefinition>(crd.Name(), cancellationToken: cancellationToken);
            if (existing is not null && !_settings.OverwriteExisting)
            {
                _logger.LogDebug("CRD {Name} already exists, skipping installation.", crd.Name());
            }
            else if (existing is not null)
            {
                _logger.LogDebug("CRD {Name} already exists.", crd.Name());
                _logger.LogInformation("Overwriting existing CRD {Name}.", crd.Name());
                crd.Metadata.ResourceVersion = existing.ResourceVersion();
                await _client.UpdateAsync(crd, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Installing CRD {Name}.", crd.Name());
                await _client.CreateAsync(crd, cancellationToken);
            }
        }
    }
}
