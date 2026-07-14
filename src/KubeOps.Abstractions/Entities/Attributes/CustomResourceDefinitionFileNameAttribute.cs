// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines the output file name used when generating the custom resource definition.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CustomResourceDefinitionFileNameAttribute(string fileName) : Attribute
{
    /// <summary>
    /// Gets the output file name, including its extension.
    /// </summary>
    public string FileName => fileName;
}
