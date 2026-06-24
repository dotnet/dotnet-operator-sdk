// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// The topology type for an array property.
/// </summary>
public enum XListType
{
    /// <summary>
    /// The list is treated as a single entity, like a scalar.
    /// Atomic lists will be entirely replaced when updated.
    /// </summary>
    Atomic,

    /// <summary>
    /// Sets are lists that must not have multiple items with the same value.
    /// </summary>
    Set,

    /// <summary>
    /// These lists are like maps in that their elements have a non-index key
    /// used to identify them. Order is preserved upon merge.
    /// </summary>
    Map,
}

/// <summary>
/// Annotates an array to further describe its topology.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class XListTypeAttribute(XListType listType) : Attribute
{
    /// <summary>
    /// The list type.
    /// </summary>
    public XListType ListType => listType;
}
