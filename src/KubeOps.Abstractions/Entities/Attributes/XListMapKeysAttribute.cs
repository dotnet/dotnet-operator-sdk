// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Annotates an array with x-kubernetes-list-type "map" by specifying the keys used as the index of the map.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class XListMapKeysAttribute(params string[] keys) : Attribute
{
    /// <summary>
    /// The keys used as the index of the map.
    /// </summary>
    public string[] Keys => keys;
}
