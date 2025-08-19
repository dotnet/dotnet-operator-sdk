// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Type for a list of entities.
/// </summary>
/// <typeparam name="T">Type for the list entries.</typeparam>
public class EntityList<T> : KubernetesObject
    where T : IKubernetesObject
{
    /// <summary>
    /// Official list metadata object of kubernetes.
    /// </summary>
    public V1ListMeta Metadata { get; set; } = new();

    /// <summary>
    /// The list of items.
    /// </summary>
    public IList<T> Items { get; set; } = new List<T>();
}
