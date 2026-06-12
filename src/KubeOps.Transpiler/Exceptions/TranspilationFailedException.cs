// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Transpiler.Exceptions;

/// <summary>
/// Raised when an entity cannot be transpiled into a CRD. The message is prefixed with the affected
/// entity; the concrete cause (for example a circular type reference or a non-transpilable type) is
/// available via <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class TranspilationFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TranspilationFailedException"/> class.
    /// </summary>
    public TranspilationFailedException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TranspilationFailedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TranspilationFailedException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TranspilationFailedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public TranspilationFailedException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
