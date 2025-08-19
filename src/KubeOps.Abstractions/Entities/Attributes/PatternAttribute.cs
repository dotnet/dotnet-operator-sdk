// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Define a regex validator for the property.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PatternAttribute(string regexPattern) : Attribute
{
    /// <summary>
    /// The regex pattern to be used.
    /// </summary>
    public string RegexPattern => regexPattern;
}
