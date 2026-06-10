// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Transpiler;

/// <summary>
/// Raised when the CRD transpiler encounters a type it cannot map to an OpenAPI schema.
/// </summary>
public sealed class InvalidTypeException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTypeException"/> class.
    /// </summary>
    public InvalidTypeException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTypeException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidTypeException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTypeException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public InvalidTypeException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
