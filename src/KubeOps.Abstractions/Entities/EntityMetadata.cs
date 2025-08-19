// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Metadata for a given entity.
/// </summary>
/// <param name="Kind">The kind of the entity (e.g. deployment).</param>
/// <param name="Version">Version (e.g. v1 or v2-alpha).</param>
/// <param name="Group">The group in Kubernetes (e.g. "testing.dev").</param>
/// <param name="Plural">An optional plural name. Defaults to the singular name with an added "s".</param>
public record EntityMetadata(string Kind, string Version, string? Group = null, string? Plural = null)
{
    /// <summary>
    /// Kind of the entity when used in a list.
    /// </summary>
    public string ListKind => $"{Kind}List";

    /// <summary>
    /// Name of the singular entity.
    /// </summary>
    public string SingularName => Kind.ToLowerInvariant();

    /// <summary>
    /// Name of the plural entity.
    /// </summary>
    public string PluralName => (Plural ?? $"{Kind}s").ToLowerInvariant();

    public string GroupWithVersion => $"{Group ?? string.Empty}/{Version}".TrimStart('/');
}
