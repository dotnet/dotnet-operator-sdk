// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Cli.Output;
using KubeOps.Operator.Web.Certificates;

namespace KubeOps.Cli.Generators;

internal class CertificateGenerator(string serverName, string namespaceName) : IConfigGenerator
{
    public void Generate(ResultOutput output)
    {
        using Operator.Web.Certificates.CertificateGenerator generator = new(serverName, namespaceName);

        output.Add("ca.pem", generator.Root.Certificate.EncodeToPem(), OutputFormat.Plain);
        output.Add("ca-key.pem", generator.Root.Key.EncodeToPem(), OutputFormat.Plain);
        output.Add("svc.pem", generator.Server.Certificate.EncodeToPem(), OutputFormat.Plain);
        output.Add("svc-key.pem", generator.Server.Key.EncodeToPem(), OutputFormat.Plain);
    }
}
