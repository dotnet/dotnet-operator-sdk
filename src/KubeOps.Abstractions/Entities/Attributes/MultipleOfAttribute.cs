// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines the factor that a numeric value must adhere to.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class MultipleOfAttribute(double value) : Attribute
{
    /// <summary>
    /// The property should be a multiple of this value.
    /// </summary>
    public double Value => value;
}
