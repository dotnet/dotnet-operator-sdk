// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines length limits for properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class LengthAttribute(long minLength = -1, long maxLength = -1) : Attribute
{
    /// <summary>
    /// Define the minimum length.
    /// </summary>
    public long? MinLength => minLength switch
    {
        -1 => null,
        _ => minLength,
    };

    /// <summary>
    /// Define the maximum length.
    /// </summary>
    public long? MaxLength => maxLength switch
    {
        -1 => null,
        _ => maxLength,
    };
}
