// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Logging;
using KubeOps.Abstractions.Reconciliation;

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
    /// Creates an unenriched logging scope for the provided Kubernetes watch event.
    /// </summary>
    /// <typeparam name="TEntity">The Kubernetes entity type.</typeparam>
    /// <param name="eventType">The watch event type.</param>
    /// <param name="entity">The entity associated with the event.</param>
    /// <returns>A logging scope containing the built-in identification fields.</returns>
    public static EntityLoggingScope CreateFor<TEntity>(WatchEventType eventType, TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => CreateUnenriched(eventType.ToString(), ReconciliationTriggerSource.ApiServer, entity);

    /// <summary>
    /// Creates an unenriched logging scope for the provided reconciliation.
    /// </summary>
    /// <typeparam name="TEntity">The Kubernetes entity type.</typeparam>
    /// <param name="eventType">The reconciliation operation type.</param>
    /// <param name="reconciliationTriggerSource">The source that triggered the reconciliation.</param>
    /// <param name="entity">The entity associated with the reconciliation.</param>
    /// <returns>A logging scope containing the built-in identification fields.</returns>
    public static EntityLoggingScope CreateFor<TEntity>(
        ReconciliationType eventType,
        ReconciliationTriggerSource reconciliationTriggerSource,
        TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => CreateUnenriched(eventType.ToString(), reconciliationTriggerSource, entity);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        => Values.GetEnumerator();

    /// <inheritdoc />
    public override string ToString()
        => CachedFormattedString ??= $"{{ {string.Join(", ", Values.Select(kvp => $"{kvp.Key} = {kvp.Value}"))} }}";

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    internal static EntityLoggingScope Create<TEntity>(
        string eventType,
        ReconciliationTriggerSource triggerSource,
        TEntity entity,
        EntityLoggingPhase phase,
        EntityLoggingScopeEnricherPipeline<TEntity> enrichers)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var items = new Dictionary<string, object>(7);
        SetBuiltInProperties(items, eventType, triggerSource, entity);

        enrichers.Enrich(entity, phase, items);

        return new(items);
    }

    private static EntityLoggingScope CreateUnenriched<TEntity>(
        string eventType,
        ReconciliationTriggerSource triggerSource,
        TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var items = new Dictionary<string, object>(7);
        SetBuiltInProperties(items, eventType, triggerSource, entity);
        return new(items);
    }

    private static void SetBuiltInProperties<TEntity>(
        IDictionary<string, object> items,
        string eventType,
        ReconciliationTriggerSource triggerSource,
        TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        items["EventType"] = eventType;
        items["ReconciliationTriggerSource"] = triggerSource;
        items[nameof(entity.Kind)] = entity.Kind;
        items["Namespace"] = entity.Namespace();
        items["Name"] = entity.Name();
        items["Uid"] = entity.Uid();
        items["ResourceVersion"] = entity.ResourceVersion();
    }
}
