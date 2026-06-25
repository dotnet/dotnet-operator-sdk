// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines a title for a property.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public class TitleAttribute(string title) : Attribute
{
    /// <summary>
    /// The given title for the property.
    /// </summary>
    public string Title => title;
}
