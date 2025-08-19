// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Rbac;

/// <summary>
/// <para>
/// Generic attribute to define rbac needs for the operator.
/// This needs get generated into rbac - yaml style resources
/// for installation on a cluster.
/// </para>
/// <para>The attribute essentially defines the role definition of kubernetes.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class GenericRbacAttribute : RbacAttribute
{
    /// <summary>
    /// <para>List of groups.</para>
    /// <para>
    /// Yaml example:
    /// "apiGroups: ...".
    /// </para>
    /// </summary>
    public string[] Groups { get; init; } = Array.Empty<string>();

    /// <summary>
    /// <para>List of resources.</para>
    /// <para>
    /// Yaml example:
    /// "resources: ["pods"]".
    /// </para>
    /// </summary>
    public string[] Resources { get; init; } = Array.Empty<string>();

    /// <summary>
    /// List of urls.
    /// </summary>
    public string[] Urls { get; init; } = Array.Empty<string>();

    /// <summary>
    /// <para>Flags ("list") of allowed verbs.</para>
    /// <para>
    /// Yaml example:
    /// "verbs: ["get", "list", "watch"]".
    /// </para>
    /// </summary>
    public RbacVerb Verbs { get; init; }
}
