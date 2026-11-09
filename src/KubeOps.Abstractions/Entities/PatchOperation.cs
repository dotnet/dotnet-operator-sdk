// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// A single operation of a JSON Patch document (RFC 6902).
/// </summary>
public sealed class PatchOperation
{
    private PatchOperation(OperationType op, string path, JsonNode? value = null)
    {
        Op = op;
        Path = path;
        Value = value?.DeepClone();
    }

    /// <summary>
    /// Gets the type of the operation.
    /// </summary>
    public OperationType Op { get; }

    /// <summary>
    /// Gets the target path as JSON Pointer (RFC 6901).
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the value of an <c>add</c> or <c>replace</c> operation.
    /// </summary>
    public JsonNode? Value { get; }

    /// <summary>
    /// Create an <c>add</c> operation.
    /// </summary>
    /// <param name="path">The target path.</param>
    /// <param name="value">The value to add. It is cloned.</param>
    /// <returns>The operation.</returns>
    public static PatchOperation Add(string path, JsonNode? value) => new(OperationType.Add, path, value);

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
    public static PatchOperation Replace(string path, JsonNode? value) => new(OperationType.Replace, path, value);
}
