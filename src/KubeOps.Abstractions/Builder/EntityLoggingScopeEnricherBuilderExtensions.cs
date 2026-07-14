// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Logging;

using Microsoft.Extensions.DependencyInjection;

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Registration helpers for <see cref="IEntityLoggingScopeEnricher{TEntity}"/> implementations that contribute
/// additional structured properties to the logging scope surrounding every watch event and reconciliation.
/// </summary>
public static class EntityLoggingScopeEnricherBuilderExtensions
{
    /// <summary>
    /// Registers a logging scope enricher applied to scopes created for <typeparamref name="TEntity"/>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type the enricher applies to.</typeparam>
    /// <typeparam name="TEnricher">The enricher implementation.</typeparam>
    /// <param name="builder">The operator builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IOperatorBuilder AddEntityLoggingScopeEnricher<TEntity, TEnricher>(this IOperatorBuilder builder)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        where TEnricher : class, IEntityLoggingScopeEnricher<TEntity>
    {
        builder.Services.AddSingleton<IEntityLoggingScopeEnricher<TEntity>, TEnricher>();
        return builder;
    }
}
