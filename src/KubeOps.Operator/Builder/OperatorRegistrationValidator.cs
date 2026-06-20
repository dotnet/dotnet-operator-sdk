// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.Operator.Exceptions;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Builder;

/// <summary>
/// Validates, on host startup, that the operator's dependency injection registrations are complete and
/// consistent with the chosen configuration. Registered only when
/// <see cref="OperatorSettings.ValidateRegistrations"/> is enabled.
/// </summary>
/// <remarks>
/// The validator inspects the registered services (it does not construct them) and, for every managed
/// entity, verifies that the components implied by the configuration are present. If anything is missing
/// it throws an <see cref="InvalidRegistrationException"/> aggregating all gaps, aborting host startup.
/// <para>
/// Validation runs in <see cref="StartingAsync"/>, i.e. the host's <c>Starting</c> phase, which completes
/// before any hosted service's <see cref="IHostedService.StartAsync"/> is invoked. A failed validation
/// therefore aborts startup before the watcher, queue consumer, leader election or CRD installer perform
/// any work — nothing has started, so there is nothing to unwind.
/// </para>
/// </remarks>
internal sealed class OperatorRegistrationValidator(
    OperatorRegistrationRegistry registry,
    OperatorSettings settings,
    ILogger<OperatorRegistrationValidator> logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (registry.ManagedEntities.Count == 0)
        {
            return Task.CompletedTask;
        }

        var problems = new List<string>();
        foreach (var entityType in registry.ManagedEntities)
        {
            ValidateEntity(entityType, problems);
        }

        if (problems.Count > 0)
        {
            throw new InvalidRegistrationException(
                "Operator registration validation failed. The following required components are not " +
                "registered for the current configuration " +
                $"(LeaderElectionType.{settings.LeaderElectionType}, QueueStrategy.{settings.QueueStrategy}):" +
                Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => $"  - {p}")) + Environment.NewLine +
                "Register the missing components or disable validation via " +
                "OperatorSettings.ValidateRegistrations.");
        }

        logger.LogDebug(
            "Operator registration validation passed for {EntityCount} managed entit(y/ies).",
            registry.ManagedEntities.Count);

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool HasService(IServiceCollection services, Type serviceType) =>
        services.Any(d => d.ServiceType == serviceType);

    private static bool HasHostedServiceDerivedFrom(IServiceCollection services, Type baseType) =>
        services.Any(d =>
        {
            if (d.ServiceType != typeof(IHostedService))
            {
                return false;
            }

            var implementationType = d.ImplementationType ?? d.ImplementationInstance?.GetType();
            return implementationType is not null && baseType.IsAssignableFrom(implementationType);
        });

    private static bool HasKeyedFinalizer(IServiceCollection services, Type entityType)
    {
        var finalizerType = typeof(IEntityFinalizer<>).MakeGenericType(entityType);
        return services.Any(d => d.IsKeyedService && d.ServiceType == finalizerType);
    }

    private void ValidateEntity(Type entityType, List<string> problems)
    {
        var services = registry.Services;
        var entityName = entityType.Name;

        // Without a controller there is no reconciliation pipeline at all, so the remaining checks would
        // only add noise. If the entity is only present because a finalizer was registered for it, point
        // at the actual mistake: finalizers run as part of reconciliation and require a controller.
        if (!HasService(services, typeof(IReconciler<>).MakeGenericType(entityType)))
        {
            problems.Add(HasKeyedFinalizer(services, entityType)
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Entity '{0}': a finalizer is registered but no controller. Finalizers run as part of " +
                    "reconciliation; register a controller via AddController<…, {0}>().",
                    entityName)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Entity '{0}': no IReconciler<{0}> is registered.",
                    entityName));
            return;
        }

        if (!HasHostedServiceDerivedFrom(services, typeof(ResourceWatcher<>).MakeGenericType(entityType)))
        {
            problems.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Entity '{0}': no resource watcher is registered (expected a hosted service deriving from ResourceWatcher<{0}>).",
                entityName));
        }

        // The queue and its consumer are only managed by the SDK for the in-memory strategy. With
        // QueueStrategy.Custom the queue is entirely user-owned and cannot be introspected, so it is
        // left unchecked.
        if (settings.QueueStrategy != QueueStrategy.InMemory)
        {
            return;
        }

        if (!HasService(services, typeof(ITimedEntityQueue<>).MakeGenericType(entityType)))
        {
            problems.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Entity '{0}': no ITimedEntityQueue<{0}> is registered.",
                entityName));
        }

        if (!HasHostedServiceDerivedFrom(services, typeof(EntityQueueBackgroundService<>).MakeGenericType(entityType)))
        {
            problems.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Entity '{0}': no queue consumer is registered (expected a hosted service deriving from EntityQueueBackgroundService<{0}>).",
                entityName));
        }
    }
}
