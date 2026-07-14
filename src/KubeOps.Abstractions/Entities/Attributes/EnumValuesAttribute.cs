// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Overrides the allowed values generated for a property in the OpenAPI schema.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EnumValuesAttribute : Attribute
{
    /// <summary>
    /// Initializes the attribute with string values.
    /// </summary>
    /// <param name="values">The allowed values.</param>
    public EnumValuesAttribute(params string[] values) => Values = values;

    /// <summary>
    /// Initializes the attribute with integer values.
    /// </summary>
    /// <param name="values">The allowed values.</param>
    public EnumValuesAttribute(params long[] values) => Values = values.Cast<object>().ToArray();

    /// <summary>
    /// Initializes the attribute with floating-point values.
    /// </summary>
    /// <param name="values">The allowed values.</param>
    public EnumValuesAttribute(params double[] values) => Values = values.Cast<object>().ToArray();

    /// <summary>
    /// Gets the configured allowed values.
    /// </summary>
    public IReadOnlyList<object> Values { get; }
}
