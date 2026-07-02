// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;

namespace KubeOps.Operator.Builder;

/// <summary>
/// Bridges build-time information to the <see cref="OperatorRegistrationValidator"/> running at host
/// startup: it tracks the entity types managed by the operator and exposes the service collection so
/// the validator can inspect the registrations.
/// </summary>
/// <param name="services">The service collection the operator is registered into.</param>
internal sealed class OperatorRegistrationRegistry(IServiceCollection services)
{
    private readonly HashSet<Type> _managedEntities = [];

    /// <summary>
    /// Gets the entity types that are managed by the operator (i.e. that have a controller registered).
    /// </summary>
    public IReadOnlyCollection<Type> ManagedEntities => _managedEntities;

    /// <summary>
    /// Gets the service collection the operator is registered into.
    /// </summary>
    public IServiceCollection Services => services;

    /// <summary>
    /// Registers an entity type as managed by the operator.
    /// </summary>
    /// <param name="entityType">The entity type to register.</param>
    public void Add(Type entityType) => _managedEntities.Add(entityType);
}
