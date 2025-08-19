// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Certificates;

/// <summary>
/// Defines properties for certificate/key pair so a custom certificate/key provider may be implemented.
/// The provider is used by the CertificateWebhookService to provide a caBundle to the webhooks.
/// </summary>
public interface ICertificateProvider : IDisposable
{
    /// <summary>
    /// The server certificate and key.
    /// </summary>
    CertificatePair Server { get; }

    /// <summary>
    /// The root certificate and key.
    /// </summary>
    CertificatePair Root { get; }
}
