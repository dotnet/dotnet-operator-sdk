// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// A single operation of a JSON Patch document (RFC 6902).
/// </summary>
/// <param name="Op">The type of the operation.</param>
/// <param name="Path">The target path as JSON Pointer (RFC 6901).</param>
/// <param name="Value">The value of an <c>add</c> or <c>replace</c> operation.</param>
public sealed record PatchOperation(OperationType Op, string Path, JsonNode? Value = null)
{
    /// <summary>
    /// Create an <c>add</c> operation.
    /// </summary>
    /// <param name="path">The target path.</param>
    /// <param name="value">The value to add. It is cloned.</param>
    /// <returns>The operation.</returns>
    public static PatchOperation Add(string path, JsonNode? value) =>
        new(OperationType.Add, path, value?.DeepClone());

    /// <summary>
    /// Create a <c>remove</c> operation.
    /// </summary>
    /// <param name="path">The target path.</param>
    /// <returns>The operation.</returns>
    public static PatchOperation Remove(string path) => new(OperationType.Remove, path);

    /// <summary>
    /// Create a <c>replace</c> operation.
    /// </summary>
    /// <param name="path">The target path.</param>
    /// <param name="value">The new value. It is cloned.</param>
    /// <returns>The operation.</returns>
    public static PatchOperation Replace(string path, JsonNode? value) =>
        new(OperationType.Replace, path, value?.DeepClone());
}
