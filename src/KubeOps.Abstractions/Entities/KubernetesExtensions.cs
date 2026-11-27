// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Basic extensions for <see cref="IKubernetesObject{TMetadata}"/>.
/// Extensions that target the Kubernetes Object and its metadata.
/// </summary>
public static class KubernetesExtensions
{
    /// <summary>
    /// Sets the resource version of the specified Kubernetes object to the specified value.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes object.</typeparam>
    /// <param name="entity">The Kubernetes object.</param>
    /// <param name="resourceVersion">The resource version to set.</param>
    /// <returns>The Kubernetes object with the updated resource version.</returns>
    public static TEntity WithResourceVersion<TEntity>(
        this TEntity entity,
        string resourceVersion)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        entity.EnsureMetadata().ResourceVersion = resourceVersion;
        return entity;
    }

    /// <summary>
    /// Sets the resource version of the specified Kubernetes object to the resource version of another object.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes object.</typeparam>
    /// <param name="entity">The Kubernetes object.</param>
    /// <param name="other">The other Kubernetes object.</param>
    /// <returns>The Kubernetes object with the updated resource version.</returns>
    public static TEntity WithResourceVersion<TEntity>(
        this TEntity entity,
        TEntity other)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        entity.EnsureMetadata().ResourceVersion = other.ResourceVersion();
        return entity;
    }

    /// <summary>
    /// Create a <see cref="V1ObjectReference"/> of a kubernetes object.
    /// </summary>
    /// <remarks>
    /// If the object carries no <c>apiVersion</c> or <c>kind</c>, as the items of a list response, they are taken
    /// from the <see cref="KubernetesEntityAttribute"/> of its type.
    /// </remarks>
    /// <param name="kubernetesObject">The object that should be translated.</param>
    /// <returns>The created <see cref="V1ObjectReference"/>.</returns>
    public static V1ObjectReference MakeObjectReference(this IKubernetesObject<V1ObjectMeta> kubernetesObject)
        => new()
        {
            ApiVersion = kubernetesObject.GetApiVersion(),
            Kind = kubernetesObject.GetKind(),
            Name = kubernetesObject.Metadata.Name,
            NamespaceProperty = kubernetesObject.Metadata.NamespaceProperty,
            ResourceVersion = kubernetesObject.Metadata.ResourceVersion,
            Uid = kubernetesObject.Metadata.Uid,
        };

    /// <summary>
    /// Adds the owner to the owner references of the resource.
    /// </summary>
    /// <remarks>
    /// A reference to the same owner (same API group, kind and name; the version is ignored) is replaced instead of
    /// added again. This also replaces a controller reference to the owner with a plain owner reference.
    /// </remarks>
    /// <param name="resource">The resource that is owned by another resource.</param>
    /// <param name="owner">The owner to add.</param>
    /// <typeparam name="TEntity">The type of the entity.</typeparam>
    /// <returns>The resource with the added owner reference.</returns>
    /// <exception cref="ArgumentException">
    /// The owner has no apiVersion, kind, name or uid, or it is namespaced and the resource has no or another
    /// namespace.
    /// </exception>
    public static TEntity WithOwnerReference<TEntity>(
        this TEntity resource,
        IKubernetesObject<V1ObjectMeta> owner)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        resource.UpsertOwnerReference(owner, owner.MakeOwnerReference());
        return resource;
    }

    /// <summary>
    /// Adds the owner as the controller of the resource: an owner reference with <c>controller: true</c> and
    /// <c>blockOwnerDeletion: true</c>.
    /// </summary>
    /// <remarks>
    /// A resource has at most one controller. A reference to the same owner (same API group, kind and name; the
    /// version is ignored) is replaced instead of added again.
    /// </remarks>
    /// <param name="resource">The resource that is controlled by the owner.</param>
    /// <param name="owner">The owner that controls the resource.</param>
    /// <typeparam name="TEntity">The type of the entity.</typeparam>
    /// <returns>The resource with the added controller reference.</returns>
    /// <exception cref="ArgumentException">
    /// The owner has no apiVersion, kind, name or uid, or it is namespaced and the resource has no or another
    /// namespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">The resource is already controlled by another owner.</exception>
    public static TEntity WithControllerReference<TEntity>(
        this TEntity resource,
        IKubernetesObject<V1ObjectMeta> owner)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var reference = owner.MakeControllerReference();
        if (resource.GetController() is { } existing && !RefersToSameObject(existing, reference))
        {
            throw new InvalidOperationException(
                $"{resource.Kind} {resource.Name()} is already controlled by {existing.Kind} {existing.Name}.");
        }

        resource.UpsertOwnerReference(owner, reference);
        return resource;
    }

    /// <summary>
    /// Create a <see cref="V1OwnerReference"/> out of a kubernetes object.
    /// </summary>
    /// <remarks>
    /// The reference sets neither <c>controller</c> nor <c>blockOwnerDeletion</c>. Use
    /// <see cref="MakeControllerReference"/> for the owner that controls the resource. If the object carries no
    /// <c>apiVersion</c> or <c>kind</c>, as the items of a list response, they are taken from the
    /// <see cref="KubernetesEntityAttribute"/> of its type.
    /// </remarks>
    /// <param name="kubernetesObject">The object that should be translated.</param>
    /// <returns>The created <see cref="V1OwnerReference"/>.</returns>
    public static V1OwnerReference MakeOwnerReference(this IKubernetesObject<V1ObjectMeta> kubernetesObject)
        => new()
        {
            ApiVersion = kubernetesObject.GetApiVersion(),
            Kind = kubernetesObject.GetKind(),
            Name = kubernetesObject.Metadata.Name,
            Uid = kubernetesObject.Metadata.Uid,
        };

    /// <summary>
    /// Create a controller reference out of a kubernetes object: a <see cref="V1OwnerReference"/> with
    /// <c>controller: true</c> and <c>blockOwnerDeletion: true</c>.
    /// </summary>
    /// <param name="kubernetesObject">The object that controls the resource.</param>
    /// <returns>The created <see cref="V1OwnerReference"/>.</returns>
    public static V1OwnerReference MakeControllerReference(this IKubernetesObject<V1ObjectMeta> kubernetesObject)
    {
        var reference = kubernetesObject.MakeOwnerReference();
        reference.Controller = true;
        reference.BlockOwnerDeletion = true;
        return reference;
    }

    /// <summary>
    /// Retrieves the <see cref="KubernetesEntityAttribute"/> applied to the specified Kubernetes object type, if it exists.
    /// </summary>
    /// <param name="entity">The Kubernetes object to inspect for the attribute.</param>
    /// <returns>The <see cref="KubernetesEntityAttribute"/> if found; otherwise, null.</returns>
    public static KubernetesEntityAttribute? GetKubernetesEntityAttribute(this IKubernetesObject<V1ObjectMeta> entity)
        => entity.GetType().GetCustomAttribute<KubernetesEntityAttribute>(true);

    private static IList<V1OwnerReference> EnsureOwnerReferences(this V1ObjectMeta meta) =>
        meta.OwnerReferences ??= new List<V1OwnerReference>();

    private static void UpsertOwnerReference(
        this IKubernetesObject<V1ObjectMeta> resource,
        IKubernetesObject<V1ObjectMeta> owner,
        V1OwnerReference reference)
    {
        // The API server rejects an owner reference without these fields.
        if (string.IsNullOrEmpty(reference.ApiVersion) || string.IsNullOrEmpty(reference.Kind)
            || string.IsNullOrEmpty(reference.Name) || string.IsNullOrEmpty(reference.Uid))
        {
            throw new ArgumentException(
                $"The owner {reference.Kind} {reference.Name} needs an apiVersion, kind, name and uid; " +
                "an owner that was not created yet has no uid.",
                nameof(owner));
        }

        // The garbage collector treats an owner in another namespace as absent and deletes the resource.
        var ownerNamespace = owner.Namespace();
        if (!string.IsNullOrEmpty(ownerNamespace))
        {
            var resourceNamespace = resource.Namespace();
            if (string.IsNullOrEmpty(resourceNamespace))
            {
                throw new ArgumentException(
                    $"{resource.Kind} {resource.Name()} has no namespace, but its owner is in namespace " +
                    $"{ownerNamespace}. A cluster-scoped resource cannot have a namespaced owner; for a " +
                    "namespaced resource, set the namespace before adding the owner.",
                    nameof(owner));
            }

            if (resourceNamespace != ownerNamespace)
            {
                throw new ArgumentException(
                    $"The owner of {resource.Kind} {resource.Name()} must be cluster-scoped or in namespace " +
                    $"{resourceNamespace}, but is in namespace {ownerNamespace}.",
                    nameof(owner));
            }
        }

        var references = resource.EnsureMetadata().EnsureOwnerReferences();
        for (var i = 0; i < references.Count; i++)
        {
            if (RefersToSameObject(references[i], reference))
            {
                references[i] = reference;
                return;
            }
        }

        references.Add(reference);
    }

    // As in apimachinery, a reference is identified by API group, kind and name; the version may differ.
    private static bool RefersToSameObject(V1OwnerReference left, V1OwnerReference right)
        => ApiGroup(left.ApiVersion) == ApiGroup(right.ApiVersion)
           && left.Kind == right.Kind
           && left.Name == right.Name;

    private static string ApiGroup(string? apiVersion)
        => apiVersion?.Split('/') is [var group, _] ? group : string.Empty;

    // Items of a list response carry no apiVersion and kind; the entity attribute of the type knows them.
    private static string? GetApiVersion(this IKubernetesObject<V1ObjectMeta> kubernetesObject)
        => string.IsNullOrEmpty(kubernetesObject.ApiVersion)
           && kubernetesObject.GetKubernetesEntityAttribute() is { } attribute
            ? new EntityMetadata(attribute.Kind, attribute.ApiVersion, attribute.Group).GroupWithVersion
            : kubernetesObject.ApiVersion;

    private static string? GetKind(this IKubernetesObject<V1ObjectMeta> kubernetesObject)
        => string.IsNullOrEmpty(kubernetesObject.Kind)
            ? kubernetesObject.GetKubernetesEntityAttribute()?.Kind ?? kubernetesObject.Kind
            : kubernetesObject.Kind;
}
