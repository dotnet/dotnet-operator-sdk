// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;

using k8s;

using Spectre.Console;

namespace KubeOps.Cli.Commands.Utilities;

internal static class Version
{
    public static Command Command
    {
        get
        {
            var cmd = new Command(
                "api-version",
                "Prints the actual server version of the connected kubernetes cluster.");
            cmd.Aliases.Add("av");
            cmd.SetAction(_ =>
                Handler(AnsiConsole.Console, new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig())));

            return cmd;
        }
    }

    internal static async Task<int> Handler(IAnsiConsole console, IKubernetes client)
    {
        var version = await client.Version.GetCodeAsync();
        console.Write(new Table()
            .Title("Kubernetes API Version")
            .HideHeaders()
            .AddColumns("Info", "Value")
            .AddRow("Git-Version", version.GitVersion)
            .AddRow("Major", version.Major)
            .AddRow("Minor", version.Minor)
            .AddRow("Platform", version.Platform));

        return ExitCodes.Success;
    }
}
