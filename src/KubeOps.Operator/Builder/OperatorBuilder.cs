// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

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
using KubeOps.Operator.Queue;
using KubeOps.Operator.Reconciliation;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KubeOps.Operator.Builder;

internal sealed class OperatorBuilder : IOperatorBuilder
{
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
    {
        Services.TryAddScoped<IEntityController<TEntity>, TImplementation>();
        Services.TryAddSingleton<ITimedEntityQueue<TEntity>, TimedEntityQueue<TEntity>>();
        Services.TryAddSingleton<IReconciler<TEntity>, Reconciler<TEntity>>();
        Services.TryAddTransient<IEntityRequeueFactory, KubeOpsEntityRequeueFactory>();
        Services.TryAddTransient<EntityRequeue<TEntity>>(services =>
            services.GetRequiredService<IEntityRequeueFactory>().Create<TEntity>());

        switch (Settings.LeaderElectionType)
        {
            case LeaderElectionType.None:
                Services.AddHostedService<EntityRequeueBackgroundService<TEntity>>();
                Services.AddHostedService<ResourceWatcher<TEntity>>();
                break;
            case LeaderElectionType.Single:
                Services.AddHostedService<EntityRequeueBackgroundService<TEntity>>();
                Services.AddHostedService<LeaderAwareResourceWatcher<TEntity>>();
                break;
        }

        return this;
    }

    public IOperatorBuilder AddController<TImplementation, TEntity, TLabelSelector>()
        where TImplementation : class, IEntityController<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
        where TLabelSelector : class, IEntityLabelSelector<TEntity>
    {
        AddController<TImplementation, TEntity>();
        Services.TryAddSingleton<IEntityLabelSelector<TEntity>, TLabelSelector>();

        return this;
    }

    public IOperatorBuilder AddFinalizer<TImplementation, TEntity>(string identifier)
        where TImplementation : class, IEntityFinalizer<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        Services.TryAddKeyedTransient<IEntityFinalizer<TEntity>, TImplementation>(identifier);
        Services.TryAddTransient<IEventFinalizerAttacherFactory, KubeOpsEventFinalizerAttacherFactory>();
        Services.TryAddTransient<EntityFinalizerAttacher<TImplementation, TEntity>>(services =>
            services.GetRequiredService<IEventFinalizerAttacherFactory>()
                .Create<TImplementation, TEntity>(identifier));

        return this;
    }

    public IOperatorBuilder AddCrdInstaller(Action<CrdInstallerSettings>? configure = null)
    {
        var settings = new CrdInstallerSettings();
        configure?.Invoke(settings);
        Services.AddSingleton(settings);
        Services.AddHostedService<CrdInstaller>();
        return this;
    }

    private void AddOperatorBase()
    {
        Services.AddSingleton(Settings);
        Services.AddSingleton(new ActivitySource(Settings.Name));

        // add and configure resource watcher entity cache
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

        Services.TryAddTransient<IEventPublisherFactory, KubeOpsEventPublisherFactory>();
        Services.TryAddTransient<EventPublisher>(services =>
            services.GetRequiredService<IEventPublisherFactory>().Create());

        Services.AddSingleton(typeof(IEntityLabelSelector<>), typeof(DefaultEntityLabelSelector<>));

        if (Settings.LeaderElectionType == LeaderElectionType.Single)
        {
            Services.AddLeaderElection();
        }
    }
}
