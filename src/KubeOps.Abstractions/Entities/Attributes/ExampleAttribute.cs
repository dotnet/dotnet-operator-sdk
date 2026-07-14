// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines an example value for a property in the generated OpenAPI schema.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ExampleAttribute : Attribute
{
    /// <summary>
    /// Initializes the attribute with a boolean value.
    /// </summary>
    /// <param name="value">The example value.</param>
    public ExampleAttribute(bool value) => Value = value;

    /// <summary>
    /// Initializes the attribute with an integer value.
    /// </summary>
    /// <param name="value">The example value.</param>
    public ExampleAttribute(long value) => Value = value;

    /// <summary>
    /// Initializes the attribute with a floating-point value.
    /// </summary>
    /// <param name="value">The example value.</param>
    public ExampleAttribute(double value) => Value = value;

    /// <summary>
    /// Initializes the attribute with a string value.
    /// </summary>
    /// <param name="value">The example value or its JSON representation when <see cref="Json"/> is set.</param>
    public ExampleAttribute(string value) => Value = value;

    /// <summary>
    /// Gets or sets whether the string value contains JSON that should be emitted as a structured value.
    /// </summary>
    public bool Json { get; init; }

    /// <summary>
    /// Gets the configured example value.
    /// </summary>
    public object Value { get; }
}
