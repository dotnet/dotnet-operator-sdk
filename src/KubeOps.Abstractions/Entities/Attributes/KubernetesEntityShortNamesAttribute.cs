// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Define "shortNames" for CRDs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class KubernetesEntityShortNamesAttribute(params string[] shortNames) : Attribute
{
    /// <summary>
    /// Array of shortnames that should be attached to CRDs.
    /// </summary>
    public string[] ShortNames => shortNames;
}
