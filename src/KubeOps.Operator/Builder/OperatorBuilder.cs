// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Diagnostics.Metrics;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Crds;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Events;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Crds;
using KubeOps.Operator.Events;
using KubeOps.Operator.Finalizer;
using KubeOps.Operator.LeaderElection;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Metrics;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Reconciliation;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Builder;

internal sealed class OperatorBuilder : IOperatorBuilder
{
    private readonly HashSet<Type> _sharedWatcherEntities = [];

    private OperatorRegistrationRegistry? _registrationRegistry;

    public OperatorBuilder(IServiceCollection services, OperatorSettings settings)
    {
        Settings = settings;
        Services = services;
        AddOperatorBase();
    }

    public IServiceCollection Services { get; }

    public OperatorSettings Settings { get; }

    public IOperatorBuilder AddController<TImplementation, TEntity>()
        where TImplementation : class, IEntityController<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => AddControllerCore<TImplementation, TEntity>(labelSelectorType: null, fieldSelectorType: null);

    public IOperatorBuilder AddControllerWithLabelSelector<TImplementation, TEntity, TLabelSelector>()
        where TImplementation : class, IEntityController<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
        where TLabelSelector : class, IEntityLabelSelector<TEntity>
        => AddControllerCore<TImplementation, TEntity>(typeof(TLabelSelector), fieldSelectorType: null);

    public IOperatorBuilder AddControllerWithFieldSelector<TImplementation, TEntity, TFieldSelector>()
        where TImplementation : class, IEntityController<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
        where TFieldSelector : class, IEntityFieldSelector<TEntity>
        => AddControllerCore<TImplementation, TEntity>(labelSelectorType: null, typeof(TFieldSelector));

    public IOperatorBuilder AddFinalizer<TImplementation, TEntity>(string identifier)
        where TImplementation : class, IEntityFinalizer<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        Services.TryAddKeyedTransient<IEntityFinalizer<TEntity>, TImplementation>(identifier);
        Services.TryAddTransient<IEventFinalizerAttacherFactory, KubeOpsEventFinalizerAttacherFactory>();
        Services.TryAddTransient<EntityFinalizerAttacher<TImplementation, TEntity>>(services =>
            services.GetRequiredService<IEventFinalizerAttacherFactory>()
                .Create<TImplementation, TEntity>(identifier));

        RegisterRegistrationValidation(typeof(TEntity));

        return this;
    }

    public IOperatorBuilder AddCrdInstaller(Action<CrdInstallerSettingsBuilder>? configure = null)
    {
        var settingsBuilder = new CrdInstallerSettingsBuilder();
        configure?.Invoke(settingsBuilder);

        Services.AddSingleton(settingsBuilder.Build());
        Services.AddHostedService<CrdInstaller>();
        return this;
    }

    private static EntityQueue<TEntity> CreateEntityQueueDelegate<TEntity>(IServiceProvider services)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var logger = services.GetService<ILogger<EntityQueue<TEntity>>>();

        return (entity, type, triggerSource, queueIn, retryCount, cancellationToken) =>
        {
            var queue = ResolveQueue();

            logger?
                .LogTrace(
                    """Queue entity "{Identifier}"{Retry} in {Seconds}s.""",
                    entity.ToIdentifierString(),
                    retryCount > 0 ? $" (Retry: {retryCount})" : string.Empty,
                    queueIn.TotalSeconds);

            return queue.Enqueue(entity, type, triggerSource, queueIn, retryCount, cancellationToken);
        };

        ITimedEntityQueue<TEntity> ResolveQueue()
        {
            // Inside a reconciliation scope, the reconciler published the originating pipeline's queue,
            // so requeues flow back into the pipeline the reconciliation came from.
            try
            {
                if (services.GetService<ActivePipelineQueue<TEntity>>()?.Current is { } activeQueue)
                {
                    return activeQueue;
                }
            }
            catch (InvalidOperationException)
            {
                // Resolved from the root provider with scope validation enabled — fall through to the
                // scope-independent resolutions below.
            }

            // QueueStrategy.Custom: the user-registered queue, shared by all pipelines of the entity.
            if (services.GetService<ITimedEntityQueue<TEntity>>() is { } customQueue)
            {
                return customQueue;
            }

            // Outside a reconciliation scope the target queue is only unambiguous with a single pipeline.
            var pipelines = services.GetServices<ControllerPipeline<TEntity>>().ToList();
            return pipelines switch
            {
                [var single] => single.Queue(services),
                [] => throw new InvalidOperationException(
                    $"No controller pipeline is registered for entity '{typeof(TEntity).Name}'."),
                _ => throw new InvalidOperationException(
                    $"Multiple controller pipelines are registered for entity '{typeof(TEntity).Name}'; " +
                    $"an EntityQueue<{typeof(TEntity).Name}> delegate resolved outside a reconciliation scope " +
                    "cannot determine the target queue. Inject the delegate into the controller (or another " +
                    "scoped service) instead."),
            };
        }
    }

    private static IHostedService CreateSharedWatcher<TEntity>(IServiceProvider services)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var settings = services.GetRequiredService<OperatorSettings>();
        var pipelines = services.GetServices<ControllerPipeline<TEntity>>()
            .Where(p => p.FieldSelectorType is null)
            .ToList();

        if (pipelines.Count == 1)
        {
            // A single pipeline shares nothing: use a dedicated watcher so its label selector is
            // applied server-side, exactly as with WatchStrategy.PerController.
            return pipelines[0].CreateWatcher(services);
        }

        var targets = pipelines
            .Select(p => new SharedPipelineDispatcher<TEntity>.PipelineTarget(
                p.Key,
                p.Queue(services),
                p.LabelSelector(services)))
            .ToList();
        var dispatcher = new SharedPipelineDispatcher<TEntity>(
            targets,
            services.GetRequiredService<ILogger<ResourceWatcher<TEntity>>>());

        // The shared watch runs without server-side selectors; matching happens client-side per
        // pipeline. The queue passed to the base constructor is unused (event dispatch is overridden),
        // but the constructor requires one.
        var labelSelector = new DefaultEntityLabelSelector<TEntity>();
        var fieldSelector = new DefaultEntityFieldSelector<TEntity>();
        var unusedQueue = targets[0].Queue;

        return settings.LeaderElectionType switch
        {
            LeaderElectionType.Single => ActivatorUtilities.CreateInstance<LeaderAwareSharedResourceWatcher<TEntity>>(
                services, unusedQueue, labelSelector, fieldSelector, dispatcher),
            _ => ActivatorUtilities.CreateInstance<SharedResourceWatcher<TEntity>>(
                services, unusedQueue, labelSelector, fieldSelector, dispatcher),
        };
    }

    private IOperatorBuilder AddControllerCore<TImplementation, TEntity>(Type? labelSelectorType, Type? fieldSelectorType)
        where TImplementation : class, IEntityController<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var pipeline = new ControllerPipeline<TEntity>(typeof(TImplementation), labelSelectorType, fieldSelectorType);

        if (Services.Any(d => d.ImplementationInstance is ControllerPipeline<TEntity> existing && existing.Key == pipeline.Key))
        {
            throw new InvalidOperationException(
                $"A controller pipeline for '{typeof(TImplementation).Name}' with the same label/field selector " +
                $"is already registered for entity '{typeof(TEntity).Name}'. Registering the identical " +
                "combination twice would open a redundant watch stream.");
        }

        // Multiple controllers per entity are only safe on the default path (in-memory queue, non-custom
        // leader election), where each pipeline owns its queue and reconciler. With QueueStrategy.Custom or
        // LeaderElectionType.Custom all pipelines share one user-owned queue and a single unkeyed
        // IReconciler<TEntity> with no per-pipeline identity, so a second controller would silently never
        // fire (issue #909). Reject it explicitly instead. LeaderElectionType.Single is unaffected.
        if ((Settings.LeaderElectionType == LeaderElectionType.Custom || Settings.QueueStrategy == QueueStrategy.Custom) &&
            Services.Any(d => d.ImplementationInstance is ControllerPipeline<TEntity>))
        {
            throw new InvalidOperationException(
                $"Entity '{typeof(TEntity).Name}' already has a controller registered. Multiple controllers " +
                "per entity are only supported with the default in-memory queue and non-custom leader " +
                "election, because QueueStrategy.Custom and LeaderElectionType.Custom share a single " +
                "user-owned queue and reconciler with no per-pipeline identity. Register at most one " +
                "controller per entity for these modes, or switch to the default queue/leader election to " +
                "run multiple controllers.");
        }

        // The reconciler resolves the controller by its concrete type, since multiple controllers may be
        // registered for the same entity type. The interface mapping is kept so existing code resolving
        // IEntityController<TEntity> still gets the first registered controller.
        Services.AddScoped<TImplementation>();
        Services.TryAddScoped<IEntityController<TEntity>, TImplementation>();

        if (labelSelectorType is not null)
        {
            Services.TryAddSingleton(labelSelectorType);
            Services.TryAddSingleton(typeof(IEntityLabelSelector<TEntity>), s => s.GetRequiredService(labelSelectorType));
        }

        if (fieldSelectorType is not null)
        {
            Services.TryAddSingleton(fieldSelectorType);
            Services.TryAddSingleton(typeof(IEntityFieldSelector<TEntity>), s => s.GetRequiredService(fieldSelectorType));
        }

        Services.AddSingleton(pipeline);
        Services.TryAddScoped<ActivePipelineQueue<TEntity>>();
        Services.TryAddTransient<EntityQueue<TEntity>>(CreateEntityQueueDelegate<TEntity>);

        RegisterRegistrationValidation(typeof(TEntity));

        // The user owns the queue consumer with custom leader election or a custom queue strategy; in
        // both cases they need an IReconciler<TEntity> (and, for the in-memory queue, an
        // ITimedEntityQueue<TEntity>) from the container to construct it — for example when deriving from
        // EntityQueueBackgroundService<TEntity>. With the default in-memory pipeline the reconciler is
        // owned by the pipeline, so this registration is never resolved there (ITimedEntityQueue<TEntity>
        // is intentionally not registered in that path). This manual wiring supports a single controller
        // per entity type.
        if (Settings.LeaderElectionType == LeaderElectionType.Custom || Settings.QueueStrategy == QueueStrategy.Custom)
        {
            if (Settings.QueueStrategy == QueueStrategy.InMemory)
            {
                Services.TryAddSingleton<ITimedEntityQueue<TEntity>, TimedEntityQueue<TEntity>>();
            }

            Services.TryAddSingleton<IReconciler<TEntity>>(services =>
                ActivatorUtilities.CreateInstance<Reconciler<TEntity>>(
                    services,
                    services.GetRequiredService<ITimedEntityQueue<TEntity>>(),
                    typeof(TImplementation)));
        }

        if (Settings.LeaderElectionType == LeaderElectionType.Custom)
        {
            // With custom leader election the user wires the watcher and queue consumer themselves.
            return this;
        }

        // The queue consumer is registered (and therefore started) before the watcher so that, under
        // leader election, its intake gate is open by the time the watcher starts enqueuing. With
        // QueueStrategy.Custom the user registers their own consumer. Hosted services are registered
        // via AddSingleton<IHostedService> (not AddHostedService) because the latter deduplicates by
        // implementation type and would drop the second pipeline's services.
        if (Settings.QueueStrategy == QueueStrategy.InMemory)
        {
            Services.AddSingleton<IHostedService>(pipeline.CreateQueueConsumer);
        }

        if (Settings.WatchStrategy == WatchStrategy.PerController || fieldSelectorType is not null)
        {
            // Dedicated watcher with server-side selectors. Pipelines with a field selector always get a
            // dedicated watcher: field selectors cannot be evaluated client-side on a shared watch.
            Services.AddSingleton<IHostedService>(pipeline.CreateWatcher);
        }
        else if (_sharedWatcherEntities.Add(typeof(TEntity)))
        {
            Services.AddSingleton<IHostedService>(CreateSharedWatcher<TEntity>);
        }

        return this;
    }

    private void AddOperatorBase()
    {
        Services.AddSingleton(Settings);
        Services.AddSingleton(new ActivitySource(Settings.Name));

        if (Settings.EnableMetrics)
        {
            Services.AddMetrics();
            Services.AddSingleton(sp =>
                new OperatorMetrics(sp.GetRequiredService<IMeterFactory>(), Settings.Name));
        }

        Services.WithResourceWatcherEntityCaching(Settings);

        // Add the default configuration and the client separately. This allows external users to override either
        // just the config (e.g. for integration tests) or to replace the whole client, e.g. with a mock.
        // We also add the k8s.IKubernetes as a singleton service, in order to allow accessing internal services
        // and also external users to make use of its features that might not be implemented in the adapted client.
        //
        // Due to a memory leak in the Kubernetes client, it is important that the client is registered with
        // the same lifetime as the KubernetesClientConfiguration. This is tracked in kubernetes/csharp#1446.
        // https://github.com/kubernetes-client/csharp/issues/1446
        //
        // The missing ability to inject a custom HTTP client and therefore the possibility to use the .AddHttpClient()
        // functionalities led us choosing Singleton as the lifetime.
        Services.TryAddSingleton(_ => KubernetesClientConfiguration.BuildDefaultConfig());
        Services.TryAddSingleton<IKubernetes>(services =>
            new Kubernetes(services.GetRequiredService<KubernetesClientConfiguration>()));
        Services.TryAddSingleton<IKubernetesClient, KubernetesClient.KubernetesClient>();

        Services.TryAddSingleton<ICrdResourceFactory, KubeOpsCrdResourceFactory>();
        Services.TryAddSingleton<IEventResourceFactory, KubeOpsEventResourceFactory>();
        Services.TryAddTransient<IEventPublisherFactory, KubeOpsEventPublisherFactory>();
        Services.TryAddTransient<EventPublisher>(services =>
            services.GetRequiredService<IEventPublisherFactory>().Create());

        Services.AddSingleton(typeof(IEntityLabelSelector<>), typeof(DefaultEntityLabelSelector<>));
        Services.AddSingleton(typeof(IEntityFieldSelector<>), typeof(DefaultEntityFieldSelector<>));

        if (Settings.LeaderElectionType == LeaderElectionType.Single)
        {
            Services.AddLeaderElection();
        }
    }

    private void RegisterRegistrationValidation(Type entityType)
    {
        if (!Settings.ValidateRegistrations)
        {
            return;
        }

        if (_registrationRegistry is null)
        {
            _registrationRegistry = new(Services);
            Services.AddSingleton(_registrationRegistry);
            Services.AddHostedService<OperatorRegistrationValidator>();
        }

        _registrationRegistry.Add(entityType);
    }
}
