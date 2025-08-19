// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.Versioning;

using k8s;
using k8s.Models;

using KubeOps.Transpiler;

namespace KubeOps.Operator.Web.Webhooks.Conversion;

/// <summary>
/// Entity converter that converts between two Kubernetes entity versions.
/// </summary>
/// <typeparam name="TTarget">Target type (version).</typeparam>
[RequiresPreviewFeatures(
    "Conversion webhooks API is not yet stable, the way that conversion " +
    "webhooks are implemented may change in the future based on user feedback.")]
public interface IEntityConverter<TTarget>
    where TTarget : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// The type of the entity that is converted from.
    /// </summary>
    Type FromType { get; }

    /// <summary>
    /// The type of the entity that is converted to.
    /// </summary>
    Type ToType => typeof(TTarget);

    /// <summary>
    /// Group/APIVersion of the entity that is converted from.
    /// </summary>
    string FromGroupVersion { get; }

    /// <summary>
    /// Group/APIVersion of the entity that is converted to.
    /// </summary>
    string ToGroupVersion => Entities.ToEntityMetadata<TTarget>().Metadata.GroupWithVersion;

    /// <summary>
    /// Forward conversion of an object to the target entity version.
    /// </summary>
    /// <param name="from">The object that will be converted.</param>
    /// <returns>The converted version result.</returns>
    TTarget Convert(object from);

    /// <summary>
    /// Revert conversion of an object to the source entity version.
    /// </summary>
    /// <param name="to">The entity.</param>
    /// <returns>The base from where the entity was converted.</returns>
    object Revert(TTarget to);
}

/// <summary>
/// Specific entity converter that converts between two Kubernetes entity versions.
/// </summary>
/// <typeparam name="TFrom">Source entity.</typeparam>
/// <typeparam name="TTo">Target entity.</typeparam>
[RequiresPreviewFeatures(
    "Conversion webhooks API is not yet stable, the way that conversion " +
    "webhooks are implemented may change in the future based on user feedback.")]
public interface IEntityConverter<TFrom, TTo> : IEntityConverter<TTo>
    where TFrom : IKubernetesObject<V1ObjectMeta>
    where TTo : IKubernetesObject<V1ObjectMeta>
{
    /// <inheritdoc />
    Type IEntityConverter<TTo>.FromType => typeof(TFrom);

    /// <inheritdoc />
    string IEntityConverter<TTo>.FromGroupVersion => Entities.ToEntityMetadata<TFrom>().Metadata.GroupWithVersion;

    /// <inheritdoc cref="IEntityConverter{TTo}.Convert(object)" />
    TTo Convert(TFrom from);

    /// <inheritdoc cref="IEntityConverter{TTo}.Revert(TTo)" />
    new TFrom Revert(TTo from);

    /// <inheritdoc />
    TTo IEntityConverter<TTo>.Convert(object from)
        => Convert((TFrom)from);

    /// <inheritdoc />
    object IEntityConverter<TTo>.Revert(TTo to)
        => Revert(to);
}
