// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Logging;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Logging;

/// <summary>
/// Applies registered enrichers in order (so the first enricher that contributes a key wins) and isolates
/// individual failures so logging-scope enrichment cannot take down a watch or reconciliation.
/// </summary>
/// <typeparam name="TEntity">The entity type this pipeline enriches scopes for.</typeparam>
internal sealed partial class EntityLoggingScopeEnricherPipeline<TEntity>(
    IEnumerable<IEntityLoggingScopeEnricher<TEntity>> entityEnrichers,
    ILogger<EntityLoggingScopeEnricherPipeline<TEntity>> logger)
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly IEntityLoggingScopeEnricher<TEntity>[] _entityEnrichers = entityEnrichers.ToArray();

    public void Enrich(TEntity entity, EntityLoggingPhase phase, IDictionary<string, object> items)
    {
        if (_entityEnrichers.Length == 0)
        {
            return;
        }

        var pendingItems = new Dictionary<string, object>();

        foreach (var enricher in _entityEnrichers)
        {
            try
            {
                enricher.Enrich(entity, phase, pendingItems);
                foreach (var item in pendingItems)
                {
                    items.TryAdd(item.Key, item.Value);
                }
            }
            catch (Exception exception)
            {
                LogEnricherFailed(exception, enricher.GetType().FullName ?? enricher.GetType().Name);
            }
            finally
            {
                pendingItems.Clear();
            }
        }
    }

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Warning,
        Message = "Entity logging scope enricher '{Enricher}' threw and was skipped.")]
    private partial void LogEnricherFailed(Exception exception, string enricher);
}
