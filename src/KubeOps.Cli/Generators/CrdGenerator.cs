// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s.Models;

using KubeOps.Abstractions.Entities.Attributes;

using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;
using KubeOps.Transpiler;

namespace KubeOps.Cli.Generators;

internal sealed class CrdGenerator(MetadataLoadContext parser, byte[] caBundle,
    OutputFormat outputFormat) : IConfigGenerator
{
    public void Generate(ResultOutput output)
    {
        var entities = parser.GetEntities().ToList();
        var crds = parser.Transpile(entities, parser.GetInheritedAttributeResolver()).ToList();
        var conversionWebhooks = parser.GetConvertedEntities().ToList();

        foreach (var crd in crds)
        {
            var hasConversionWebhook = conversionWebhooks
                .Find(wh => crd.Spec.Group == wh.Group && crd.Spec.Names.Kind == wh.Kind) is not null;

            ConfigureConversion(crd, hasConversionWebhook, caBundle);

            output.Add(GetFileName(crd, entities), crd);
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

    internal static string ResolveFileName(
        string customResourceDefinitionName,
        OutputFormat format,
        IReadOnlyList<string?> configuredNames)
    {
        return configuredNames switch
        {
            { Count: > 1 } => throw new InvalidOperationException(
                $"The versions of CRD '{customResourceDefinitionName}' specify conflicting output file names."),
            [var fileName] when string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName =>
                throw new InvalidOperationException(
                    $"The configured output file name for CRD '{customResourceDefinitionName}' " +
                    "must be a non-empty file name."),
            [var fileName] => fileName!,
            _ => $"{customResourceDefinitionName.Replace('.', '_')}.{format.GetFileExtension()}",
        };
    }

    private string GetFileName(V1CustomResourceDefinition crd, IEnumerable<Type> entities)
    {
        var configuredNames = entities
            .Where(entity =>
            {
                var metadata = parser.ToEntityMetadata(entity).Metadata;
                return $"{metadata.PluralName}.{metadata.Group}" == crd.Metadata.Name;
            })
            .Select(entity => entity.GetCustomAttributeData<CustomResourceDefinitionFileNameAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.GetCustomAttributeCtorArg<string>(parser, 0))
            .Where(fileName => fileName is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return ResolveFileName(crd.Metadata.Name, outputFormat, configuredNames);
    }
}
