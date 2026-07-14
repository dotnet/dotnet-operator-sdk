// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Logging;

/// <summary>
/// Contributes additional structured properties to logging scopes created for <typeparamref name="TEntity"/>.
/// Enrichers can add properties, but cannot change or remove existing properties.
/// </summary>
/// <typeparam name="TEntity">The entity type this enricher applies to.</typeparam>
/// <remarks>
/// Register via <c>builder.AddEntityLoggingScopeEnricher&lt;TEntity, TEnricher&gt;()</c>. Enrichers are resolved
/// as singletons, may be invoked concurrently, and run synchronously on the hot path.
/// </remarks>
public interface IEntityLoggingScopeEnricher<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>Adds properties to the logging scope being built for <typeparamref name="TEntity"/>.</summary>
    /// <param name="entity">The concrete entity the scope is being created for.</param>
    /// <param name="phase">The pipeline stage in which the scope is being created.</param>
    /// <param name="properties">The properties contributed by this enricher.</param>
    void Enrich(TEntity entity, EntityLoggingPhase phase, IDictionary<string, object> properties);
}
