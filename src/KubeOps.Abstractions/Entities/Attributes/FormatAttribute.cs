// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Overrides the OpenAPI format generated for a property.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FormatAttribute(string? format) : Attribute
{
    /// <summary>
    /// Gets the OpenAPI format.
    /// </summary>
    public string? Format => format;
}
