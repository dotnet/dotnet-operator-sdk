// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

namespace KubeOps.Abstractions.Webhooks;

/// <summary>
/// Factory interface for creating webhook configuration resources.
/// Implement this interface to customize the shape of mutating and validating webhook configurations.
/// </summary>
public interface IWebhookConfigurationFactory
{
    /// <summary>
    /// Creates a <see cref="V1MutatingWebhookConfiguration"/> from the given registrations.
    /// </summary>
    /// <param name="registrations">The collection of mutating webhook registrations.</param>
    /// <returns>The generated <see cref="V1MutatingWebhookConfiguration"/>.</returns>
    V1MutatingWebhookConfiguration CreateMutatingConfiguration(IEnumerable<MutatingWebhookRegistration> registrations);

    /// <summary>
    /// Creates a <see cref="V1ValidatingWebhookConfiguration"/> from the given registrations.
    /// </summary>
    /// <param name="registrations">The collection of validating webhook registrations.</param>
    /// <returns>The generated <see cref="V1ValidatingWebhookConfiguration"/>.</returns>
    V1ValidatingWebhookConfiguration CreateValidatingConfiguration(IEnumerable<ValidatingWebhookRegistration> registrations);
}
