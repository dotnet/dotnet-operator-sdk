// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// The operation types of a JSON Patch document (RFC 6902).
/// </summary>
public enum OperationType
{
    /// <summary>
    /// Adds a value at the target path.
    /// </summary>
    Add,

    /// <summary>
    /// Removes the value at the target path.
    /// </summary>
    Remove,

    /// <summary>
    /// Replaces the value at the target path.
    /// </summary>
    Replace,

    /// <summary>
    /// Moves the value at the source path to the target path.
    /// </summary>
    Move,

    /// <summary>
    /// Copies the value at the source path to the target path.
    /// </summary>
    Copy,

    /// <summary>
    /// Tests that the value at the target path equals the given value.
    /// </summary>
    Test,
}
