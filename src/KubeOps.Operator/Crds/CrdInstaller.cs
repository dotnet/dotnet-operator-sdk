using System.Reflection;
using System.Runtime.InteropServices;

using k8s.Models;

using KubeOps.Abstractions.Crds;
using KubeOps.Abstractions.Entities.Attributes;
using KubeOps.KubernetesClient;
using KubeOps.Transpiler;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Crds;

internal class CrdInstaller(ILogger<CrdInstaller> logger, CrdInstallerSettings settings, IKubernetesClient client)
    : IHostedService
{
    private List<V1CustomResourceDefinition> _crds = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Execute CRD installer with overwrite: {Overwrite}", settings.OverwriteExisting);
        var assembly = Assembly.GetEntryAssembly();
        if (assembly is null)
        {
            logger.LogError("No entry assembly found, cannot install CRDs.");
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
                await client.GetAsync<V1CustomResourceDefinition>(crd.Name(), cancellationToken: cancellationToken);
            if (existing is not null && !settings.OverwriteExisting)
            {
                logger.LogDebug("CRD {Name} already exists, skipping installation.", crd.Name());
            }
            else if (existing is not null)
            {
                logger.LogDebug("CRD {Name} already exists.", crd.Name());
                logger.LogInformation("Overwriting existing CRD {Name}.", crd.Name());
                crd.Metadata.ResourceVersion = existing.ResourceVersion();
                await client.UpdateAsync(crd, cancellationToken);
            }
            else
            {
                logger.LogInformation("Installing CRD {Name}.", crd.Name());
                await client.CreateAsync(crd, cancellationToken);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!settings.DeleteOnShutdown)
        {
            logger.LogDebug("Skipping CRD deletion on shutdown as per settings.");
            return;
        }

        logger.LogInformation("Deleting CRDs on shutdown.");
        foreach (var crd in _crds)
        {
            try
            {
                logger.LogInformation("Deleting CRD {Name}.", crd.Name());
                await client.DeleteAsync(crd, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete CRD {Name}.", crd.Name());
            }
        }
    }
}
