// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Kustomize;
using KubeOps.Cli.Generators;
using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;

using Spectre.Console;

namespace KubeOps.Cli.Commands.Generator;

internal static class OperatorGenerator
{
    public static Command Command
    {
        get
        {
            var cmd =
                new Command(
                    "operator",
                    "Generates all required resources and configs for the operator to be built and run.")
                {
                    Options.ClearOutputPath,
                    Options.OutputFormat,
                    Options.OutputPath,
                    Options.SolutionProjectRegex,
                    Options.TargetFramework,
                    Options.AccessibleDockerImage,
                    Options.AccessibleDockerTag,
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
        var name = parseResult.GetValue(Arguments.OperatorName) ?? "operator";
        var file = parseResult.GetValue(Arguments.SolutionOrProjectFile);
        var outPath = parseResult.GetValue(Options.OutputPath);
        var format = parseResult.GetValue(Options.OutputFormat);
        var dockerImage = parseResult.GetValue(Options.AccessibleDockerImage)!;
        var dockerImageTag = parseResult.GetValue(Options.AccessibleDockerTag)!;

        var result = new ResultOutput(console, format);
        console.WriteLine("Generate operator resources.");

        console.MarkupLine("[green]Load Project/Solution file.[/]");
        var parser = file switch
        {
            { Extension: ".csproj", Exists: true } => await AssemblyLoader.ForProject(console, file),
            { Extension: ".sln", Exists: true } => await AssemblyLoader.ForSolution(
                console,
                file,
                parseResult.GetValue(Options.SolutionProjectRegex),
                parseResult.GetValue(Options.TargetFramework)),
            { Exists: false } => throw new FileNotFoundException($"The file {file.Name} does not exist."),
            _ => throw new NotSupportedException("Only *.csproj and *.sln files are supported."),
        };

        var mutators = parser.GetMutatedEntities().ToList();
        var validators = parser.GetValidatedEntities().ToList();
        var hasWebhooks = mutators.Count > 0 || validators.Count > 0 || parser.GetConvertedEntities().Any();

        console.MarkupLine("[green]Generate RBAC rules.[/]");
        new RbacGenerator(parser, format).Generate(result);

        console.MarkupLine("[green]Generate Dockerfile.[/]");
        new DockerfileGenerator(hasWebhooks).Generate(result);

        if (hasWebhooks)
        {
            console.MarkupLine(
                "[yellow]The operator contains webhooks of some sort, generating webhook operator specific resources.[/]");

            console.MarkupLine("[green]Generate CA and Server certificates.[/]");
            new CertificateGenerator(name, $"{name}-system").Generate(result);

            console.MarkupLine("[green]Generate Deployment and Service.[/]");
            new WebhookDeploymentGenerator(format).Generate(result);

            var caBundle =
                Encoding.ASCII.GetBytes(
                    Convert.ToBase64String(Encoding.ASCII.GetBytes(result["ca.pem"].ToString() ?? string.Empty)));

            console.MarkupLine("[green]Generate Validation Webhooks.[/]");
            new ValidationWebhookGenerator(validators, caBundle, format).Generate(result);

            console.MarkupLine("[green]Generate Mutation Webhooks.[/]");
            new MutationWebhookGenerator(mutators, caBundle, format).Generate(result);

            console.MarkupLine("[green]Generate CRDs.[/]");
            new CrdGenerator(parser, caBundle, format).Generate(result);
        }
        else
        {
            console.MarkupLine("[green]Generate Deployment.[/]");
            new DeploymentGenerator(format).Generate(result);

            console.MarkupLine("[green]Generate CRDs.[/]");
            new CrdGenerator(parser, [], format).Generate(result);
        }

        result.Add(
            $"namespace.{format.GetFileExtension()}",
            new V1Namespace(metadata: new(name: "system")).Initialize());

        result.Add(
            $"kustomization.{format.GetFileExtension()}",
            new KustomizationConfig
            {
                NamePrefix = $"{name}-",
                Namespace = $"{name}-system",
                Labels = [new KustomizationCommonLabels(new Dictionary<string, string> { { "operator", name }, })],
                Resources = result.DefaultFormatFiles.ToList(),
                Images =
                    new List<KustomizationImage>
                    {
                        new() { Name = "operator", NewName = dockerImage, NewTag = dockerImageTag, },
                    },
                ConfigMapGenerator = hasWebhooks
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
                SecretGenerator = hasWebhooks
                    ? new List<KustomizationSecretGenerator>
                    {
                        new() { Name = "webhook-ca", Files = new List<string> { "ca.pem", "ca-key.pem", }, },
                        new() { Name = "webhook-cert", Files = new List<string> { "svc.pem", "svc-key.pem", }, },
                    }
                    : null,
            });

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
                    console.MarkupLine($"[red]Could not clear output path: {e.Message}[/]");
                }
            }

            console.MarkupLine($"[green]Write output to {outPath}.[/]");
            await result.Write(outPath);
        }
        else
        {
            result.Write();
        }

        return ExitCodes.Success;
    }
}
