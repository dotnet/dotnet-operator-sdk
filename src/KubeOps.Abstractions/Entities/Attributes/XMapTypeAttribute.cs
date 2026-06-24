// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// The topology type for an object property.
/// </summary>
public enum XMapType
{
    /// <summary>
    /// These maps are actual maps (key-value pairs) and each field is independent
    /// from each other. This is the default behaviour for all maps.
    /// </summary>
    Granular,

    /// <summary>
    /// The map is treated as a single entity, like a scalar.
    /// Atomic maps will be entirely replaced when updated.
    /// </summary>
    Atomic,
}

/// <summary>
/// Annotates an object to further describe its topology.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class XMapTypeAttribute(XMapType mapType) : Attribute
{
    /// <summary>
    /// The map type.
    /// </summary>
    public XMapType MapType => mapType;
}
