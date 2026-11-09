// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// A JSON Patch document (RFC 6902).
/// </summary>
/// <param name="Operations">The operations of the patch.</param>
public sealed record JsonPatch(IReadOnlyList<PatchOperation> Operations)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonPatch"/> class.
    /// </summary>
    /// <param name="operations">The operations of the patch.</param>
    public JsonPatch(params PatchOperation[] operations)
        : this((IReadOnlyList<PatchOperation>)operations)
    {
    }
}
