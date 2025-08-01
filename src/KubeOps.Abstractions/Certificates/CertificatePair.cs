// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace KubeOps.Abstractions.Certificates;

public record CertificatePair(X509Certificate2 Certificate, AsymmetricAlgorithm Key);
