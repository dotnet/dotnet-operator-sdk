// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.Text;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Kustomize;
using KubeOps.Cli.Extensions;
using KubeOps.Cli.Generators;
using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;

using Spectre.Console;

namespace KubeOps.Cli.Commands.Generator;

internal static class OperatorGenerator
{
    private const string CommandName = "operator";
    private const string OperatorName = "operator";

    public static Command Command
    {
        get
        {
            var cmd =
                new Command(
                    CommandName,
                    "Generates all required resources and configs for the operator to be built and run.")
                {
                    Options.ClearOutputPath,
                    Options.OutputFormat,
                    Options.OutputPath,
                    Options.SolutionProjectRegex,
                    Options.TargetFramework,
                    Options.AccessibleDockerImage,
                    Options.AccessibleDockerTag,
                    Options.NoAnsi,
                    Options.OperatorNamespace,
                    Options.OperatorResources,
                    Arguments.OperatorName,
                    Arguments.SolutionOrProjectFile,
                };
            cmd.Aliases.Add("op");
            cmd.SetAction(result => Handler(AnsiConsole.Console, result));

            return cmd;
        }
    }

    internal static async Task<int> Handler(IAnsiConsole console, ParseResult parseResult)
    {
        console.ApplyOptions(parseResult);

        var name = parseResult.GetValue(Arguments.OperatorName) ?? OperatorName;
        var file = parseResult.GetValue(Arguments.SolutionOrProjectFile);
        var outPath = parseResult.GetValue(Options.OutputPath);
        var format = parseResult.GetValue(Options.OutputFormat);
        var dockerImage = parseResult.GetValue(Options.AccessibleDockerImage)!;
        var dockerImageTag = parseResult.GetValue(Options.AccessibleDockerTag)!;
        var operatorNamespace = parseResult.GetValue(Options.OperatorNamespace);
        var resources = parseResult.GetValue(Options.OperatorResources) ?? [OperatorResource.All];
        var selectedResources = resources.ToHashSet();
        var generateAll = selectedResources.Contains(OperatorResource.All);
        bool ShouldGenerate(OperatorResource resource) => generateAll || selectedResources.Contains(resource);
        var effectiveNamespace = operatorNamespace ?? $"{name}-system";

        var result = new ResultOutput(console, format);
        console.WriteLine("Generate operator resources.");

        console.MarkupLine("[green]Load Project/Solution file.[/]");
        var parser = file switch
        {
            { Extension: ".csproj", Exists: true } => await AssemblyLoader.ForProject(console, file),
            { Extension: ".sln" or ".slnx", Exists: true } => await AssemblyLoader.ForSolution(
                console,
                file,
                parseResult.GetValue(Options.SolutionProjectRegex),
                parseResult.GetValue(Options.TargetFramework)),
            { Exists: false } => throw new FileNotFoundException($"The file {file.Name} does not exist."),
            _ => throw new NotSupportedException("Only *.csproj, *.sln, and *.slnx files are supported."),
        };

        var mutators = parser.GetMutatedEntities().ToList();
        var validators = parser.GetValidatedEntities().ToList();
        var hasAdmissionWebhooks = mutators.Count > 0 || validators.Count > 0;
        var hasConversionWebhooks = parser.GetConvertedEntities().Any();
        var hasWebhooks = hasAdmissionWebhooks || hasConversionWebhooks;

        if (ShouldGenerate(OperatorResource.Rbac))
        {
            console.MarkupLine("[green]Generate RBAC rules.[/]");
            new RbacGenerator(parser, format, effectiveNamespace).Generate(result);
        }

        if (ShouldGenerate(OperatorResource.Dockerfile))
        {
            console.MarkupLine("[green]Generate Dockerfile.[/]");
            new DockerfileGenerator(hasWebhooks).Generate(result);
        }

        if (hasWebhooks)
        {
            var requiresCertificates = RequiresCertificates(
                ShouldGenerate(OperatorResource.Certificates),
                ShouldGenerate(OperatorResource.Webhooks),
                ShouldGenerate(OperatorResource.Crds),
                hasAdmissionWebhooks,
                hasConversionWebhooks);
            ResultOutput? certificateOutput = null;
            if (requiresCertificates)
            {
                console.MarkupLine(
                    "[yellow]The operator contains webhooks of some sort, generating required certificate material.[/]");
                certificateOutput = ShouldGenerate(OperatorResource.Certificates)
                    ? result
                    : new ResultOutput(console, format);
                new CertificateGenerator($"{name}-{OperatorName}", effectiveNamespace).Generate(certificateOutput);
            }

            var caBundle = certificateOutput is null
                ? []
                : Encoding.ASCII.GetBytes(certificateOutput["ca.pem"].ToString() ?? string.Empty);

            if (ShouldGenerate(OperatorResource.Deployment))
            {
                console.MarkupLine("[green]Generate Deployment and Service.[/]");
                new WebhookDeploymentGenerator(format).Generate(result);
            }

            if (ShouldGenerate(OperatorResource.Webhooks))
            {
                console.MarkupLine("[green]Generate Validation Webhooks.[/]");
                new ValidationWebhookGenerator(validators, caBundle, format).Generate(result);

                console.MarkupLine("[green]Generate Mutation Webhooks.[/]");
                new MutationWebhookGenerator(mutators, caBundle, format).Generate(result);
            }

            if (ShouldGenerate(OperatorResource.Crds))
            {
                console.MarkupLine("[green]Generate CRDs.[/]");
                new CrdGenerator(parser, caBundle, format).Generate(result);
            }
        }
        else
        {
            if (ShouldGenerate(OperatorResource.Deployment))
            {
                console.MarkupLine("[green]Generate Deployment.[/]");
                new DeploymentGenerator(format).Generate(result);
            }

            if (ShouldGenerate(OperatorResource.Crds))
            {
                console.MarkupLine("[green]Generate CRDs.[/]");
                new CrdGenerator(parser, [], format).Generate(result);
            }
        }

        if (operatorNamespace is null && ShouldGenerate(OperatorResource.Namespace))
        {
            result.Add(
                $"namespace.{format.GetFileExtension()}",
                new V1Namespace { Metadata = new() { Name = "system" } }.Initialize());
        }

        if (ShouldGenerate(OperatorResource.Kustomization))
        {
            result.Add(
                $"kustomization.{format.GetFileExtension()}",
                new KustomizationConfig
                {
                    NamePrefix = $"{name}-",
                    Namespace = effectiveNamespace,
                    Labels = [new(new Dictionary<string, string> { { OperatorName, name }, })],
                    Resources = result.DefaultFormatFiles.ToList(),
                    Images =
                        new List<KustomizationImage>
                        {
                            new() { Name = OperatorName, NewName = dockerImage, NewTag = dockerImageTag, },
                        },
                    ConfigMapGenerator = hasWebhooks && ShouldGenerate(OperatorResource.Deployment)
                        ? new List<KustomizationConfigMapGenerator>
                        {
                            new()
                            {
                                Name = "webhook-config",
                                Literals = new List<string>
                                {
                                    "KESTREL__ENDPOINTS__HTTP__URL=http://0.0.0.0:5000",
                                    "KESTREL__ENDPOINTS__HTTPS__URL=https://0.0.0.0:5001",
                                    "KESTREL__ENDPOINTS__HTTPS__CERTIFICATE__PATH=/certs/svc.pem",
                                    "KESTREL__ENDPOINTS__HTTPS__CERTIFICATE__KEYPATH=/certs/svc-key.pem",
                                },
                            },
                        }
                        : null,
                    SecretGenerator = hasWebhooks && ShouldGenerate(OperatorResource.Certificates)
                        ? new List<KustomizationSecretGenerator>
                        {
                            new() { Name = "webhook-ca", Files = new List<string> { "ca.pem", "ca-key.pem", }, },
                            new() { Name = "webhook-cert", Files = new List<string> { "svc.pem", "svc-key.pem", }, },
                        }
                        : null,
                });
        }

        if (outPath is not null)
        {
            if (parseResult.GetValue(Options.ClearOutputPath))
            {
                console.MarkupLine("[yellow]Clear output path.[/]");
                try
                {
                    Directory.Delete(outPath, true);
                }
                catch (DirectoryNotFoundException)
                {
                    // the dir is not present, so we don't need to delete it.
                }
                catch (Exception e)
                {
                    console.MarkupLineInterpolated($"[red]Could not clear output path: {e.Message}[/]");
                }
            }

            console.MarkupLineInterpolated($"[green]Write output to {outPath}.[/]");
            await result.Write(outPath);
        }
        else
        {
            result.Write();
        }

        return ExitCodes.Success;
    }

    internal static bool RequiresCertificates(
        bool generatesCertificates,
        bool generatesWebhookConfigurations,
        bool generatesCrds,
        bool hasAdmissionWebhooks,
        bool hasConversionWebhooks)
        => generatesCertificates ||
           (generatesWebhookConfigurations && hasAdmissionWebhooks) ||
           (generatesCrds && hasConversionWebhooks);
}
