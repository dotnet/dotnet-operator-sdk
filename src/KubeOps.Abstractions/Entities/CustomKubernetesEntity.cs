// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Base class for custom Kubernetes entities. The interface <see cref="IKubernetesObject{TMetadata}"/>
/// can be used on its own, but this class provides convenience initializers.
/// </summary>
public abstract class CustomKubernetesEntity : KubernetesObject, IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// The metadata of the kubernetes object.
    /// </summary>
    public V1ObjectMeta Metadata { get; set; } = new();
}
