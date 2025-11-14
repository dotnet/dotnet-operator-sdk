// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Queue;

namespace KubeOps.Operator.Logging;

#pragma warning disable CA1710
/// <summary>
/// A logging scope that encapsulates contextual information related to a Kubernetes entity and event type.
/// Provides a mechanism for structured logging with key-value pairs corresponding to entity metadata and event type.
/// </summary>
public sealed record EntityLoggingScope : IReadOnlyCollection<KeyValuePair<string, object>>
#pragma warning restore CA1710
{
    private EntityLoggingScope(IReadOnlyDictionary<string, object> state)
    {
        Values = state;
    }

    public int Count => Values.Count;

    private string? CachedFormattedString { get; set; }

    private IReadOnlyDictionary<string, object> Values { get; }

    /// <summary>
    /// Creates a new instance of <see cref="EntityLoggingScope"/> for the provided Kubernetes entity and event type.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The type of the Kubernetes entity. Must implement <see cref="IKubernetesObject{V1ObjectMeta}"/>.
    /// </typeparam>
    /// <param name="eventType">
    /// The type of the watch event for the entity (e.g., Added, Modified, Deleted, or Bookmark).
    /// </param>
    /// <param name="entity">
    /// The Kubernetes entity associated with the logging scope. This includes metadata such as Kind, Namespace, Name, UID, and ResourceVersion.
    /// </param>
    /// <returns>
    /// A new <see cref="EntityLoggingScope"/> instance containing contextual key-value pairs
    /// related to the event type and the provided Kubernetes entity.
    /// </returns>
    public static EntityLoggingScope CreateFor<TEntity>(WatchEventType eventType, TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => CreateLoggingScope(eventType.ToString(), ReconciliationTriggerSource.ApiServer, entity);

    /// <summary>
    /// Creates a new instance of <see cref="EntityLoggingScope"/> for the given Kubernetes entity and requeue event type.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The type of the Kubernetes entity. Must implement <see cref="IKubernetesObject{V1ObjectMeta}"/>.
    /// </typeparam>
    /// <param name="eventType">
    /// The type of the requeue operation for the entity (e.g., Added, Modified, or Deleted).
    /// </param>
    /// <param name="entity">
    /// The Kubernetes entity associated with the logging scope. This includes metadata such as Kind, Namespace, Name, UID, and ResourceVersion.
    /// </param>
    /// <returns>
    /// A new <see cref="EntityLoggingScope"/> instance containing contextual key-value pairs
    /// related to the requeue event type and the provided Kubernetes entity.
    /// </returns>
    public static EntityLoggingScope CreateFor<TEntity>(RequeueType eventType, TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => CreateLoggingScope(eventType.ToString(), ReconciliationTriggerSource.Operator, entity);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        => Values.GetEnumerator();

    /// <inheritdoc />
    public override string ToString()
        => CachedFormattedString ??= $"{{ {string.Join(", ", Values.Select(kvp => $"{kvp.Key} = {kvp.Value}"))} }}";

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    private static EntityLoggingScope CreateLoggingScope<TEntity>(string eventType, ReconciliationTriggerSource triggerSource, TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => new(
            new Dictionary<string, object>
            {
                { "EventType", eventType },
                { "ReconciliationTriggerSource", triggerSource },
                { nameof(entity.Kind), entity.Kind },
                { "Namespace", entity.Namespace() },
                { "Name", entity.Name() },
                { "Uid", entity.Uid() },
                { "ResourceVersion", entity.ResourceVersion() },
            });
}
