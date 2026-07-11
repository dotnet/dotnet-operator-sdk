// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Reconciliation;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Builder;

/// <summary>
/// Represents one controller registration for an entity type: the controller implementation together
/// with its (optional) label and field selectors. Each pipeline owns its own queue, reconciler,
/// watcher, and queue consumer, so multiple controllers for the same entity type run fully independent
/// <c>watch → queue → reconcile</c> pipelines. Registered as a singleton instance; multiple pipelines
/// per entity type coexist.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type this pipeline reconciles.</typeparam>
internal sealed class ControllerPipeline<TEntity>(Type controllerType, Type? labelSelectorType, Type? fieldSelectorType)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly object _lock = new();

    private ITimedEntityQueue<TEntity>? _queue;
    private IReconciler<TEntity>? _reconciler;

    public Type ControllerType => controllerType;

    public Type? LabelSelectorType => labelSelectorType;

    public Type? FieldSelectorType => fieldSelectorType;

    /// <summary>
    /// Gets the human-readable pipeline identity (controller/label-selector/field-selector triple).
    /// Used for diagnostics and duplicate-registration detection.
    /// </summary>
    public string Key { get; } =
        $"{controllerType.FullName}/{labelSelectorType?.FullName ?? "-"}/{fieldSelectorType?.FullName ?? "-"}";

    /// <summary>
    /// Gets the short token that partitions the shared per-entity deduplication cache per pipeline.
    /// Derived deterministically from <see cref="Key"/>; only needs to be unique per process (the dedup
    /// cache is in-memory and empty after a restart).
    /// </summary>
    public string CachePartition { get; } = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{controllerType.FullName}/{labelSelectorType?.FullName ?? "-"}/{fieldSelectorType?.FullName ?? "-"}")))[..8];

    /// <summary>
    /// Gets this pipeline's queue. Memoized so the watcher (enqueue), the queue consumer (dequeue), and
    /// the reconciler (requeue) share the same instance. With <see cref="QueueStrategy.InMemory"/> each
    /// pipeline gets its own queue; with <see cref="QueueStrategy.Custom"/> the user-registered
    /// <see cref="ITimedEntityQueue{TEntity}"/> is resolved (and therefore shared between pipelines).
    /// </summary>
    /// <param name="services">The root service provider.</param>
    /// <returns>The pipeline's queue.</returns>
    public ITimedEntityQueue<TEntity> Queue(IServiceProvider services)
    {
        lock (_lock)
        {
            return _queue ??= services.GetRequiredService<OperatorSettings>().QueueStrategy switch
            {
                QueueStrategy.InMemory => ActivatorUtilities.CreateInstance<TimedEntityQueue<TEntity>>(services),
                _ => services.GetRequiredService<ITimedEntityQueue<TEntity>>(),
            };
        }
    }

    /// <summary>
    /// Resolves this pipeline's label selector: the configured concrete type, or the registered
    /// default when the pipeline has none.
    /// </summary>
    /// <param name="services">The root service provider.</param>
    /// <returns>The label selector.</returns>
    public IEntityLabelSelector<TEntity> LabelSelector(IServiceProvider services) =>
        labelSelectorType is null
            ? services.GetRequiredService<IEntityLabelSelector<TEntity>>()
            : (IEntityLabelSelector<TEntity>)services.GetRequiredService(labelSelectorType);

    /// <summary>
    /// Resolves this pipeline's field selector: the configured concrete type, or the registered
    /// default when the pipeline has none.
    /// </summary>
    /// <param name="services">The root service provider.</param>
    /// <returns>The field selector.</returns>
    public IEntityFieldSelector<TEntity> FieldSelector(IServiceProvider services) =>
        fieldSelectorType is null
            ? services.GetRequiredService<IEntityFieldSelector<TEntity>>()
            : (IEntityFieldSelector<TEntity>)services.GetRequiredService(fieldSelectorType);

    /// <summary>
    /// Gets this pipeline's reconciler (memoized), bound to this pipeline's queue and controller type.
    /// </summary>
    /// <param name="services">The root service provider.</param>
    /// <returns>The pipeline's reconciler.</returns>
    public IReconciler<TEntity> Reconciler(IServiceProvider services)
    {
        lock (_lock)
        {
            return _reconciler ??= ActivatorUtilities.CreateInstance<Reconciler<TEntity>>(
                services,
                Queue(services),
                controllerType);
        }
    }

    /// <summary>
    /// Creates the queue-consuming background service for this pipeline, leadership-aware when the
    /// operator uses <see cref="LeaderElectionType.Single"/>.
    /// </summary>
    /// <param name="services">The root service provider.</param>
    /// <returns>The hosted service consuming this pipeline's queue.</returns>
    public IHostedService CreateQueueConsumer(IServiceProvider services) =>
        services.GetRequiredService<OperatorSettings>().LeaderElectionType switch
        {
            LeaderElectionType.Single => ActivatorUtilities.CreateInstance<LeaderAwareEntityQueueBackgroundService<TEntity>>(
                services, Queue(services), Reconciler(services)),
            _ => ActivatorUtilities.CreateInstance<EntityQueueBackgroundService<TEntity>>(
                services, Queue(services), Reconciler(services)),
        };

    /// <summary>
    /// Creates the dedicated resource watcher for this pipeline (server-side selectors), leadership-aware
    /// when the operator uses <see cref="LeaderElectionType.Single"/>.
    /// </summary>
    /// <param name="services">The root service provider.</param>
    /// <returns>The hosted service watching for this pipeline.</returns>
    public IHostedService CreateWatcher(IServiceProvider services) =>
        services.GetRequiredService<OperatorSettings>().LeaderElectionType switch
        {
            LeaderElectionType.Single => ActivatorUtilities.CreateInstance<LeaderAwareResourceWatcher<TEntity>>(
                services, Queue(services), LabelSelector(services), FieldSelector(services), CachePartition),
            _ => ActivatorUtilities.CreateInstance<ResourceWatcher<TEntity>>(
                services, Queue(services), LabelSelector(services), FieldSelector(services), CachePartition),
        };
}
