// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using KubeOps.Abstractions.Certificates;

namespace KubeOps.Operator.Web.Certificates;

public static class CertificateExtensions
{
    /// <summary>
    /// Encodes the certificate in PEM format for use in Kubernetes.
    /// </summary>
    /// <param name="certificate">The certificate to encode.</param>
    /// <returns>The byte representation of the PEM-encoded certificate.</returns>
    public static byte[] EncodeToPemBytes(this X509Certificate2 certificate) => Encoding.UTF8.GetBytes(certificate.EncodeToPem());

    /// <summary>
    /// Encodes the certificate in PEM format.
    /// </summary>
    /// <param name="certificate">The certificate to encode.</param>
    /// <returns>The string representation of the PEM-encoded certificate.</returns>
    public static string EncodeToPem(this X509Certificate2 certificate) => new(PemEncoding.Write("CERTIFICATE", certificate.RawData));

    /// <summary>
    /// Encodes the key in PEM format.
    /// </summary>
    /// <param name="key">The key to encode.</param>
    /// <returns>The string representation of the PEM-encoded key.</returns>
    public static string EncodeToPem(this AsymmetricAlgorithm key) => new(PemEncoding.Write("PRIVATE KEY", key.ExportPkcs8PrivateKey()));

    /// <summary>
    /// Generates a new server certificate with its private key attached, and sets <see cref="X509KeyStorageFlags.PersistKeySet"/>.
    /// For example, this certificate can be used in development environments to configure <see cref="Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions"/>.
    /// </summary>
    /// <param name="serverPair">The cert/key tuple to attach.</param>
    /// <returns>An <see cref="X509Certificate2"/> with the private key attached.</returns>
    /// <exception cref="NotImplementedException">The <see cref="AsymmetricAlgorithm"/> does not have a CopyWithPrivateKey method, or the
    /// method has not been implemented in this extension.</exception>
    public static X509Certificate2 CopyServerCertWithPrivateKey(this CertificatePair serverPair)
    {
        const string? password = null;
        using X509Certificate2 temp = serverPair.Key switch
        {
            ECDsa ecdsa => serverPair.Certificate.CopyWithPrivateKey(ecdsa),
            RSA rsa => serverPair.Certificate.CopyWithPrivateKey(rsa),
            ECDiffieHellman ecdh => serverPair.Certificate.CopyWithPrivateKey(ecdh),
            DSA dsa => serverPair.Certificate.CopyWithPrivateKey(dsa),
            _ => throw new NotImplementedException($"{serverPair.Key} is not implemented for {nameof(CopyServerCertWithPrivateKey)}"),
        };

#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(
            temp.Export(X509ContentType.Pfx, password),
            password,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
#else
        return new X509Certificate2(
            temp.Export(X509ContentType.Pfx, password),
            password,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
#endif
    }
}
