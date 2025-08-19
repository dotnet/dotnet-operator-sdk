// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines a range minimum for a numeric property.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class RangeMinimumAttribute(double minimum, bool exclusiveMinimum = false) : Attribute
{
    /// <summary>
    /// Minimum value to be set.
    /// </summary>
    public double Minimum => minimum;

    /// <summary>
    /// Defines if the minimum value is included or excluded.
    /// </summary>
    public bool ExclusiveMinimum => exclusiveMinimum;
}
