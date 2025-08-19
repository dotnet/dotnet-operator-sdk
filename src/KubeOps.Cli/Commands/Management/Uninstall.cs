// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;

using k8s;
using k8s.Autorest;
using k8s.Models;

using KubeOps.Cli.Transpilation;
using KubeOps.Transpiler;

using Spectre.Console;

namespace KubeOps.Cli.Commands.Management;

internal static class Uninstall
{
    public static Command Command
    {
        get
        {
            var cmd =
                new Command("uninstall", "Uninstall CRDs from the cluster of the actually selected context.")
                {
                    Options.Force,
                    Options.SolutionProjectRegex,
                    Options.TargetFramework,
                    Arguments.SolutionOrProjectFile,
                };
            cmd.Aliases.Add("u");
            cmd.SetAction(result => Handler(
                AnsiConsole.Console,
                new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig()),
                result));

            return cmd;
        }
    }

    internal static async Task<int> Handler(IAnsiConsole console, IKubernetes client, ParseResult parseResult)
    {
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

        console.WriteLine($"Uninstall CRDs from {file.Name}.");
        var crds = parser.Transpile(parser.GetEntities()).ToList();
        if (crds.Count == 0)
        {
            console.WriteLine("No CRDs found. Exiting.");
            return ExitCodes.Success;
        }

        console.WriteLine($"Found {crds.Count} CRDs.");
        if (!force && !await console.ConfirmAsync("[red]Should the CRDs be uninstalled?[/]", false))
        {
            return ExitCodes.Aborted;
        }

        console.WriteLine($"""Starting uninstall from cluster with url "{client.BaseUri}".""");

        foreach (var crd in crds)
        {
            console.MarkupLineInterpolated(
                $"""Uninstall [cyan]"{crd.Spec.Group}/{crd.Spec.Names.Kind}"[/] from the cluster.""");

            try
            {
                switch (await client.ApiextensionsV1.ListCustomResourceDefinitionAsync(
                            fieldSelector: $"metadata.name={crd.Name()}"))
                {
                    case { Items: [var existing] }:
                        await client.ApiextensionsV1.DeleteCustomResourceDefinitionAsync(existing.Name());
                        console.MarkupLineInterpolated(
                            $"""[green]CRD "{crd.Spec.Group}/{crd.Spec.Names.Kind}" deleted.[/]""");
                        break;
                    default:
                        console.MarkupLineInterpolated(
                            $"""[green]CRD "{crd.Spec.Group}/{crd.Spec.Names.Kind}" did not exist.[/]""");
                        break;
                }
            }
            catch (HttpOperationException)
            {
                console.WriteLine(
                    $"""[red]There was a http (api) error while uninstalling "{crd.Spec.Group}/{crd.Spec.Names.Kind}".[/]""");
                throw;
            }
            catch (Exception)
            {
                console.WriteLine(
                    $"""[red]There was an error while uninstalling "{crd.Spec.Group}/{crd.Spec.Names.Kind}".[/]""");
                throw;
            }
        }

        return ExitCodes.Success;
    }
}
