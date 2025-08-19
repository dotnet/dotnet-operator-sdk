// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Rbac;

/// <summary>
/// Generate rbac information for a type.
/// Attach this attribute to a controller with the type reference to
/// a custom entity to define rbac needs for this given type(s).
/// </summary>
/// <example>
/// Allow the operator "ALL" access to the V1TestEntity.
/// <code>
/// [EntityRbac(typeof(V1TestEntity), Verbs = RbacVerb.All)]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class EntityRbacAttribute(params Type[] entities) : RbacAttribute
{
    /// <summary>
    /// List of types that this rbac verbs are valid.
    /// </summary>
    public IEnumerable<Type> Entities => entities;

    /// <summary>
    /// <para>Flags ("list") of allowed verbs.</para>
    /// <para>
    /// Yaml example:
    /// "verbs: ["get", "list", "watch"]".
    /// </para>
    /// </summary>
    public RbacVerb Verbs { get; init; }
}
