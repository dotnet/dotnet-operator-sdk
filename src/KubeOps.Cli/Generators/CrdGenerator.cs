// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s.Models;

using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;
using KubeOps.Transpiler;

namespace KubeOps.Cli.Generators;

internal sealed class CrdGenerator(MetadataLoadContext parser, byte[] caBundle,
    OutputFormat outputFormat) : IConfigGenerator
{
    public void Generate(ResultOutput output)
    {
        var crds = parser.Transpile(parser.GetEntities(), parser.GetInheritedAttributeResolver()).ToList();
        var conversionWebhooks = parser.GetConvertedEntities().ToList();

        foreach (var crd in crds)
        {
            var hasConversionWebhook = conversionWebhooks
                .Find(wh => crd.Spec.Group == wh.Group && crd.Spec.Names.Kind == wh.Kind) is not null;

            ConfigureConversion(crd, hasConversionWebhook, caBundle);

            output.Add($"{crd.Metadata.Name.Replace('.', '_')}.{outputFormat.GetFileExtension()}", crd);
        }
    }

    internal static void ConfigureConversion(
        V1CustomResourceDefinition crd,
        bool hasConversionWebhook,
        byte[] caBundle)
    {
        crd.Spec.Conversion = hasConversionWebhook
            ? new()
            {
                Strategy = "Webhook",
                Webhook = new()
                {
                    ConversionReviewVersions = ["v1"],
                    ClientConfig = new()
                    {
                        CaBundle = caBundle,
                        Service = new()
                        {
                            Path = $"/convert/{crd.Spec.Group}/{crd.Spec.Names.Plural}",
                            Name = "service",
                        },
                    },
                },
            }
            : new() { Strategy = "None" };
    }
}
