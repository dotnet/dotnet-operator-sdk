// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;

using k8s;
using k8s.Autorest;
using k8s.Models;

using KubeOps.Cli.Extensions;
using KubeOps.Cli.Transpilation;
using KubeOps.Transpiler;

using Spectre.Console;

namespace KubeOps.Cli.Commands.Management;

internal static class Install
{
    public static Command Command
    {
        get
        {
            var cmd =
                new Command("install", "Install CRDs into the cluster of the actually selected context.")
                {
                    Options.Force,
                    Options.SolutionProjectRegex,
                    Options.TargetFramework,
                    Options.NoAnsi,
                    Arguments.SolutionOrProjectFile,
                };
            cmd.Aliases.Add("i");
            cmd.SetAction(result => Handler(
                AnsiConsole.Console,
                new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig()),
                result));

            return cmd;
        }
    }

    internal static async Task<int> Handler(IAnsiConsole console, IKubernetes client, ParseResult parseResult)
    {
        console.ApplyOptions(parseResult);

        var file = parseResult.GetValue(Arguments.SolutionOrProjectFile);
        var force = parseResult.GetValue(Options.Force);

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

        console.WriteLine($"Install CRDs from {file.Name}.");
        var crds = parser.Transpile(parser.GetEntities()).ToList();
        if (crds.Count == 0)
        {
            console.WriteLine("No CRDs found. Exiting.");
            return ExitCodes.Success;
        }

        console.WriteLine($"Found {crds.Count} CRDs.");
        console.WriteLine($"""Starting install into cluster with url "{client.BaseUri}".""");

        foreach (var crd in crds)
        {
            console.MarkupLineInterpolated(
                $"""Install [cyan]"{crd.Spec.Group}/{crd.Spec.Names.Kind}"[/] into the cluster.""");

            try
            {
                switch (await client.ApiextensionsV1.ListCustomResourceDefinitionAsync(
                            fieldSelector: $"metadata.name={crd.Name()}"))
                {
                    case { Items: [var existing] }:
                        console.MarkupLineInterpolated(
                            $"""[yellow]CRD "{crd.Spec.Group}/{crd.Spec.Names.Kind}" already exists.[/]""");
                        if (!force && !await console.ConfirmAsync("[yellow]Should the CRD be overwritten?[/]"))
                        {
                            return ExitCodes.Aborted;
                        }

                        crd.Metadata.ResourceVersion = existing.ResourceVersion();
                        await client.ApiextensionsV1.ReplaceCustomResourceDefinitionAsync(crd, crd.Name());
                        break;
                    default:
                        await client.ApiextensionsV1.CreateCustomResourceDefinitionAsync(crd);
                        break;
                }

                console.MarkupLineInterpolated(
                    $"""[green]Installed / Updated CRD "{crd.Spec.Group}/{crd.Spec.Names.Kind}".[/]""");
            }
            catch (HttpOperationException)
            {
                console.WriteLine(
                    $"""[red]There was a http (api) error while installing "{crd.Spec.Group}/{crd.Spec.Names.Kind}".[/]""");
                throw;
            }
            catch (Exception)
            {
                console.WriteLine(
                    $"""[red]There was an error while installing "{crd.Spec.Group}/{crd.Spec.Names.Kind}".[/]""");
                throw;
            }
        }

        return ExitCodes.Success;
    }
}
