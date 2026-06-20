// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Operator.Exceptions;

/// <summary>
/// Raised on host startup when the operator's dependency injection registrations are incomplete or
/// inconsistent with the configured leader election and queue strategy. Thrown by the registration
/// validation enabled via <see cref="Abstractions.Builder.OperatorSettings.ValidateRegistrations"/>.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so existing handlers that catch the latter keep
/// working, while allowing callers to catch this more specific type.
/// </remarks>
public sealed class InvalidRegistrationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidRegistrationException"/> class.
    /// </summary>
    public InvalidRegistrationException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidRegistrationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidRegistrationException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidRegistrationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public InvalidRegistrationException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
