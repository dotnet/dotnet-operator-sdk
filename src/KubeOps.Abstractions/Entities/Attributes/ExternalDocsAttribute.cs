// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines that the property has an external documentation.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ExternalDocsAttribute(string url, string? description = null) : Attribute
{
    /// <summary>
    /// Additional description.
    /// </summary>
    public string? Description => description;

    /// <summary>
    /// Url where to find the documentation.
    /// </summary>
    public string Url => url;
}
