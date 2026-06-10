// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Transpiler;

/// <summary>
/// Raised when the CRD transpiler detects a circular type reference that cannot be represented as a
/// finite OpenAPI schema. Derives from <see cref="InvalidOperationException"/> to preserve backwards
/// compatibility for existing catch clauses.
/// </summary>
public sealed class CircularTypeReferenceException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CircularTypeReferenceException"/> class.
    /// </summary>
    public CircularTypeReferenceException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CircularTypeReferenceException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public CircularTypeReferenceException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CircularTypeReferenceException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public CircularTypeReferenceException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
