// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;

using KubeOps.Cli.Commands.Generator;
using KubeOps.Cli.Commands.Management;

using Version = KubeOps.Cli.Commands.Utilities.Version;

var parseResult = new RootCommand(
    "CLI for KubeOps. Commandline tool to help with management tasks such as generating or installing CRDs.")
{
    Generate.Command, Version.Command, Install.Command, Uninstall.Command,
}.Parse(args);
if (parseResult.Action is ParseErrorAction errorAction)
{
    errorAction.ShowHelp = true;
    errorAction.ShowTypoCorrections = true;
}

await parseResult.InvokeAsync();
