// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Define minimum and maximum items count for an array property.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ItemsAttribute(long minItems = -1, long maxItems = -1) : Attribute
{
    /// <summary>
    /// Defines the minimal item count for the property.
    /// </summary>
    public long? MinItems => minItems switch
    {
        -1 => null,
        _ => minItems,
    };

    /// <summary>
    /// Defines the maximal item count for the property.
    /// </summary>
    public long? MaxItems => maxItems switch
    {
        -1 => null,
        _ => maxItems,
    };
}
