// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace KubeOps.Transpiler;

/// <summary>
/// Transpiler for Kubernetes entities to create entity metadata.
/// </summary>
public static class Entities
{
    /// <summary>
    /// Create a metadata / scope tuple out of a given entity type via externally loaded assembly.
    /// </summary>
    /// <param name="context">The context that loaded the types.</param>
    /// <param name="entityType">The type to convert.</param>
    /// <returns>A tuple that contains <see cref="EntityMetadata"/> and a scope.</returns>
    /// <exception cref="ArgumentException">Thrown when the type contains no <see cref="KubernetesEntityAttribute"/>.</exception>
    public static (EntityMetadata Metadata, string Scope) ToEntityMetadata(
        this MetadataLoadContext context,
        Type entityType)
        => (context.GetContextType(entityType).GetCustomAttributeData<KubernetesEntityAttribute>(),
                context.GetContextType(entityType).GetCustomAttributeData<EntityScopeAttribute>()) switch
        {
            (null, _) => throw new ArgumentException("The given type is not a valid Kubernetes entity."),
            ({ } attr, var scope) => (new(
                    Defaulted(
                        attr.GetCustomAttributeNamedArg<string>(context, nameof(KubernetesEntityAttribute.Kind)),
                        entityType.Name),
                    Defaulted(
                        attr.GetCustomAttributeNamedArg<string>(
                            context,
                            nameof(KubernetesEntityAttribute.ApiVersion)),
                        "v1"),
                    attr.GetCustomAttributeNamedArg<string>(context, nameof(KubernetesEntityAttribute.Group)),
                    attr.GetCustomAttributeNamedArg<string>(context, nameof(KubernetesEntityAttribute.PluralName))),
                scope switch
                {
                    null => Enum.GetName(EntityScope.Namespaced) ?? "Namespaced",
                    _ => Enum.GetName(
                        scope.GetCustomAttributeCtorArg<EntityScope>(context, 0)) ?? "Namespaced",
                }),
        };

    /// <summary>
    /// Create a metadata / scope tuple out of a given entity type via reflection in the same loaded assembly.
    /// </summary>
    /// <param name="entityType">The type to convert.</param>
    /// <returns>A tuple that contains <see cref="EntityMetadata"/> and a scope.</returns>
    /// <exception cref="ArgumentException">Thrown when the type contains no <see cref="KubernetesEntityAttribute"/>.</exception>
    public static (EntityMetadata Metadata, string Scope) ToEntityMetadata(Type entityType)
        => (entityType.GetCustomAttributeData<KubernetesEntityAttribute>(),
                entityType.GetCustomAttributeData<EntityScopeAttribute>()) switch
        {
            (null, _) => throw new ArgumentException("The given type is not a valid Kubernetes entity."),
            ({ } attr, var scope) => (new(
                    Defaulted(
                        attr.GetCustomAttributeNamedArg<string>(nameof(KubernetesEntityAttribute.Kind)),
                        entityType.Name),
                    Defaulted(
                        attr.GetCustomAttributeNamedArg<string>(nameof(KubernetesEntityAttribute.ApiVersion)),
                        "v1"),
                    attr.GetCustomAttributeNamedArg<string>(nameof(KubernetesEntityAttribute.Group)),
                    attr.GetCustomAttributeNamedArg<string>(nameof(KubernetesEntityAttribute.PluralName))),
                scope switch
                {
                    null => Enum.GetName(EntityScope.Namespaced) ?? "Namespaced",
                    _ => Enum.GetName(
                        scope.GetCustomAttributeCtorArg<EntityScope>(0)) ?? "Namespaced",
                }),
        };

    /// <summary>
    /// Create a metadata / scope tuple out of a given entity type via reflection in the same loaded assembly.
    /// </summary>
    /// <typeparam name="TEntity">The type to convert.</typeparam>
    /// <returns>A tuple that contains <see cref="EntityMetadata"/> and a scope.</returns>
    /// <exception cref="ArgumentException">Thrown when the type contains no <see cref="KubernetesEntityAttribute"/>.</exception>
    public static (EntityMetadata Metadata, string Scope) ToEntityMetadata<TEntity>()
        => (typeof(TEntity).GetCustomAttributeData<KubernetesEntityAttribute>(),
                typeof(TEntity).GetCustomAttributeData<EntityScopeAttribute>()) switch
        {
            (null, _) => throw new ArgumentException("The given type is not a valid Kubernetes entity."),
            ({ } attr, var scope) => (new(
                    Defaulted(
                        attr.GetCustomAttributeNamedArg<string>(nameof(KubernetesEntityAttribute.Kind)),
                        typeof(TEntity).Name),
                    Defaulted(
                        attr.GetCustomAttributeNamedArg<string>(nameof(KubernetesEntityAttribute.ApiVersion)),
                        "v1"),
                    attr.GetCustomAttributeNamedArg<string>(nameof(KubernetesEntityAttribute.Group)),
                    attr.GetCustomAttributeNamedArg<string>(nameof(KubernetesEntityAttribute.PluralName))),
                scope switch
                {
                    null => Enum.GetName(EntityScope.Namespaced) ?? "Namespaced",
                    _ => Enum.GetName(
                        scope.GetCustomAttributeCtorArg<EntityScope>(0)) ?? "Namespaced",
                }),
        };

    private static string Defaulted(string? value, string defaultValue) =>
        string.IsNullOrWhiteSpace(value) ? defaultValue : value;
}
