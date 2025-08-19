// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using KubeOps.Abstractions.Certificates;

namespace KubeOps.Operator.Web.Certificates;

/// <summary>
/// Generates a self-signed CA certificate and server certificate using ECDsa that can be used for operator webhooks.
/// </summary>
public class CertificateGenerator : ICertificateProvider
{
    private readonly string _serverName;
    private readonly string? _serverNamespace;
    private readonly DateTime _startDate;
    private readonly DateTime _endDate;
    private readonly Lazy<CertificatePair> _root;
    private readonly Lazy<CertificatePair> _server;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateGenerator"/> class.
    /// </summary>
    /// <param name="serverName">The hostname, IP, or FQDN of the machine running the operator.</param>
    public CertificateGenerator(string serverName)
    {
        _serverName = serverName;
        _serverNamespace = null;
        _startDate = DateTime.UtcNow.Date;
        _endDate = _startDate.AddYears(5);
        _root = new(GenerateRootCertificate);
        _server = new(GenerateServerCertificate);
    }

    /// <summary>
    /// <inheritdoc cref="CertificateGenerator(string)"/>
    /// </summary>
    /// <param name="serverName"><inheritdoc cref="CertificateGenerator(string)" path="/param[@name='serverName']"/></param>
    /// <param name="serverNamespace">The Kubernetes namespace the server will run in.</param>
    public CertificateGenerator(string serverName, string serverNamespace)
        : this(serverName)
    {
        _serverNamespace = serverNamespace;
    }

    public CertificatePair Root => _root.Value;

    public CertificatePair Server => _server.Value;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_root.IsValueCreated)
        {
            _root.Value.Certificate.Dispose();
            _root.Value.Key.Dispose();
        }

        if (_server.IsValueCreated)
        {
            _server.Value.Certificate.Dispose();
            _server.Value.Key.Dispose();
        }
    }

    private CertificatePair GenerateRootCertificate()
    {
        ECDsa? key = null;
        X509Certificate2? cert = null;
        try
        {
            // Create an ECDsa key and a certificate request
            key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                "CN=Operator Root CA, C=DEV, L=Kubernetes",
                key,
                HashAlgorithmName.SHA512);

            // Specify certain details of how the certificate can be used
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.KeyEncipherment,
                    true));

            // Create the self-signed cert
            cert = request.CreateSelfSigned(_startDate, _endDate);
            return new(cert, key);
        }
        catch
        {
            key?.Dispose();
            cert?.Dispose();
            throw;
        }
    }

    private CertificatePair GenerateServerCertificate()
    {
        ECDsa? key = null;
        X509Certificate2? cert = null;
        try
        {
            key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                "CN=Operator Service, C=DEV, L=Kubernetes",
                key,
                HashAlgorithmName.SHA512);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));

            // Key purpose: clientAuth and serverAuth
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")],
                    false));
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(
                    Root.Certificate.PublicKey,
                    false));
            request.CertificateExtensions.Add(
                new CustomX509AuthorityKeyIdentifierExtension(
                    Root.Certificate,
                    false));

            // If a server namespace is provided, it's safe to assume that the operator will be running in the Kubernetes cluster
            // Otherwise, just try to parse whatever is there (i.e. for development)
            var sanBuilder = new SubjectAlternativeNameBuilder();
            if (_serverNamespace != null)
            {
                sanBuilder.AddDnsName($"{_serverName}.{_serverNamespace}.svc");
                sanBuilder.AddDnsName($"*.{_serverNamespace}.svc");
                sanBuilder.AddDnsName("*.svc");
            }
            else if (IPAddress.TryParse(_serverName, out IPAddress? ipAddress))
            {
                sanBuilder.AddIpAddress(ipAddress);
            }
            else
            {
                sanBuilder.AddDnsName(_serverName);
            }

            request.CertificateExtensions.Add(sanBuilder.Build());

            // Generate using the root certificate
            X509SignatureGenerator generator = X509SignatureGenerator
                .CreateForECDsa(Root.Certificate.GetECDsaPrivateKey()!);

            // Generate
            cert = request.Create(
                Root.Certificate.SubjectName,
                generator,
                _startDate,
                _endDate,
                Guid.NewGuid().ToByteArray());

            return new(cert, key);
        }
        catch
        {
            key?.Dispose();
            cert?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Custom class for implementing a slim version of the .NET7/8 X509AuthorityKeyIdentifierExtension class.
    /// </summary>
    private sealed class CustomX509AuthorityKeyIdentifierExtension(X509Certificate2 certificate, bool critical)
        : X509Extension(new Oid("2.5.29.35"), CreateFromCertificate(certificate), critical)
    {
        // https://source.dot.net/#System.Security.Cryptography/System/Security/Cryptography/X509Certificates/X509AuthorityKeyIdentifierExtension.cs
        // This .NET code is shipped with .NET 7/8, but is not in .NET 6, which is still supported by operator
        // The method below uses portions of the static methods CreateFromCertificate() and Create()
        private static byte[] CreateFromCertificate(X509Certificate2 certificate)
        {
            X509SubjectKeyIdentifierExtension skid =
                (X509SubjectKeyIdentifierExtension?)certificate.Extensions["2.5.29.14"] ??
                new X509SubjectKeyIdentifierExtension(certificate.PublicKey, false);

            byte[] skidBytes = Convert.FromHexString(skid.SubjectKeyIdentifier!);

            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                writer.WriteOctetString(skidBytes, new Asn1Tag(TagClass.ContextSpecific, 0));

                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4)))
                {
                    writer.WriteEncodedValue(certificate.IssuerName.RawData);
                }

                byte[] serialBytes = Convert.FromHexString(certificate.SerialNumber);
                writer.WriteInteger(serialBytes, new Asn1Tag(TagClass.ContextSpecific, 2));
            }

            return writer.Encode();
        }
    }
}
