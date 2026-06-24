// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines property count limits for object properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PropertyLimitsAttribute(long minProperties = -1, long maxProperties = -1) : Attribute
{
    /// <summary>
    /// Define the minimum number of properties.
    /// </summary>
    public long? MinProperties => minProperties switch
    {
        -1 => null,
        _ => minProperties,
    };

    /// <summary>
    /// Define the maximum number of properties.
    /// </summary>
    public long? MaxProperties => maxProperties switch
    {
        -1 => null,
        _ => maxProperties,
    };
}
