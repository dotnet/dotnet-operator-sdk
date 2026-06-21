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

    // True if a closed registration for the service exists, or an open-generic one the DI container would
    // close to it (e.g. AddSingleton(typeof(ITimedEntityQueue<>), typeof(MyQueue<>))). Keyed registrations
    // are ignored: the watcher/reconciler take these as plain (unkeyed) constructor dependencies, so a keyed
    // registration would not satisfy them.
    private static bool HasService(IServiceCollection services, Type serviceType)
    {
        if (services.Any(d => !d.IsKeyedService && d.ServiceType == serviceType))
        {
            return true;
        }

        if (!serviceType.IsGenericType)
        {
            return false;
        }

        // An open-generic registration only satisfies the requested closed type if its implementation can
        // actually be closed to it. An open implementation whose generic constraints exclude the entity (e.g.
        // `where TEntity : ISomeMarker`) would pass a name-only match but fail to resolve at runtime; verify it
        // closes. Factory/instance registrations expose no implementation type to check and are assumed to match.
        var openServiceType = serviceType.GetGenericTypeDefinition();
        return services.Any(d =>
            !d.IsKeyedService && d.ServiceType == openServiceType && ClosesToRequestedType(d, serviceType));
    }

    private static bool ClosesToRequestedType(ServiceDescriptor descriptor, Type closedServiceType)
    {
        if (descriptor.ImplementationType is not { IsGenericTypeDefinition: true } openImplementation)
        {
            return true;
        }

        try
        {
            openImplementation.MakeGenericType(closedServiceType.GenericTypeArguments);
            return true;
        }
        catch (ArgumentException)
        {
            // Generic constraints not satisfiable for this entity; the registration cannot serve it.
            return false;
        }
    }

    private static bool HasHostedServiceAssignableTo(IServiceCollection services, Type targetType) =>
        services.Any(d =>
        {
            if (d.IsKeyedService || d.ServiceType != typeof(IHostedService))
            {
                return false;
            }

            var implementationType = d.ImplementationType ?? d.ImplementationInstance?.GetType();
            return implementationType is not null && targetType.IsAssignableFrom(implementationType);
        });

    private static bool HasKeyedFinalizer(IServiceCollection services, Type entityType)
    {
        var finalizerType = typeof(IEntityFinalizer<>).MakeGenericType(entityType);
        return services.Any(d => d.IsKeyedService && d.ServiceType == finalizerType);
    }

    // The effective implementation type for a service (the last registration wins in DI). Handles closed
    // registrations as well as open-generic ones (closing the open implementation to the requested type).
    // Returns null for factory-registered services whose concrete type cannot be determined without
    // constructing them.
    private static Type? GetImplementationType(IServiceCollection services, Type serviceType)
    {
        var descriptor = services.LastOrDefault(d => !d.IsKeyedService && d.ServiceType == serviceType);
        if (descriptor is not null)
        {
            return descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
        }

        if (!serviceType.IsGenericType)
        {
            return null;
        }

        var openDescriptor = services.LastOrDefault(d =>
            !d.IsKeyedService && d.ServiceType == serviceType.GetGenericTypeDefinition());
        if (openDescriptor?.ImplementationType is { IsGenericTypeDefinition: true } openImplementation)
        {
            try
            {
                return openImplementation.MakeGenericType(serviceType.GenericTypeArguments);
            }
            catch (ArgumentException)
            {
                // Generic constraints not satisfiable for this entity; treat as undeterminable.
                return null;
            }
        }

        return openDescriptor?.ImplementationInstance?.GetType();
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

        // Hosted services are recognised by their registered implementation type. A component registered
        // through a DI factory delegate exposes no type and is therefore reported as missing. Register the
        // watcher and consumer with a concrete type so validation can inspect them.
        if (!HasHostedServiceAssignableTo(services, typeof(ResourceWatcher<>).MakeGenericType(entityType)))
        {
            problems.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Entity '{0}': no resource watcher is registered (expected a hosted service deriving from ResourceWatcher<{0}>).",
                entityName));
        }

        // ITimedEntityQueue<TEntity> is always required: both the resource watcher and the reconciler take
        // it as a constructor dependency, regardless of queue strategy. With QueueStrategy.Custom the SDK
        // does not register it, so a user who forgets to supply one would only fail at host startup with a
        // DI error.
        var queueType = typeof(ITimedEntityQueue<>).MakeGenericType(entityType);
        var queueRegistered = HasService(services, queueType);
        if (!queueRegistered)
        {
            problems.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Entity '{0}': no ITimedEntityQueue<{0}> is registered.",
                entityName));
        }

        // A queue consumer is always required. Under Single leader election it must be leadership-aware
        // (drive the queue gate), so a stronger marker is required there; otherwise the base consumer marker
        // is enough. The SDK registers one for the in-memory strategy; with QueueStrategy.Custom the user
        // supplies it and marks it accordingly.
        var single = settings.LeaderElectionType == LeaderElectionType.Single;
        var consumerType = single
            ? typeof(ILeaderAwareEntityQueueConsumer<>).MakeGenericType(entityType)
            : typeof(IEntityQueueConsumer<>).MakeGenericType(entityType);
        if (!HasHostedServiceAssignableTo(services, consumerType))
        {
            problems.Add(single
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Entity '{0}': no leadership-aware queue consumer is registered. LeaderElectionType.Single " +
                    "requires a consumer implementing ILeaderAwareEntityQueueConsumer<{0}> (e.g. deriving from " +
                    "LeaderAwareEntityQueueBackgroundService<{0}>) so the queue gate is driven on leadership " +
                    "transitions.",
                    entityName)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Entity '{0}': no queue consumer is registered (expected a hosted service implementing " +
                    "IEntityQueueConsumer<{0}>, e.g. deriving from EntityQueueBackgroundService<{0}>).",
                    entityName));
        }

        // Under Single leader election the queue must support the leadership gate so a former leader leaves
        // no work behind on a leadership transition.
        if (single && queueRegistered)
        {
            var queueImpl = GetImplementationType(services, queueType);
            if (queueImpl is null)
            {
                // The queue is registered but its concrete type cannot be determined (e.g. a DI factory),
                // so the gate capability cannot be verified. Fail rather than silently assume it is safe —
                // consistent with how an unverifiable consumer is handled.
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Entity '{0}': the registered queue cannot be inspected for {1} (it is registered via a " +
                    "factory delegate). LeaderElectionType.Single requires a queue whose leadership-gate " +
                    "capability can be verified — register it with a concrete or open-generic type, or use the " +
                    "built-in TimedEntityQueue<{0}>.",
                    entityName,
                    nameof(ISuspendableEntityQueue)));
            }
            else if (!typeof(ISuspendableEntityQueue).IsAssignableFrom(queueImpl))
            {
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Entity '{0}': the registered queue ({1}) does not implement {2}, which " +
                    "LeaderElectionType.Single requires for leadership-loss protection (queue clear and intake " +
                    "suspension). Implement {2} on your queue or use the built-in TimedEntityQueue<{0}>.",
                    entityName,
                    queueImpl.Name,
                    nameof(ISuspendableEntityQueue)));
            }
        }
    }
}
