// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines a description for a property.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public class DescriptionAttribute(string description) : Attribute
{
    /// <summary>
    /// The given description for the property.
    /// </summary>
    public string Description => description;
}
