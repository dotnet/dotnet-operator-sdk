// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.Text.RegularExpressions;

using KubeOps.Cli.Output;

namespace KubeOps.Cli;

internal static class Options
{
    public static readonly Option<OutputFormat> OutputFormat = new("--format")
    {
        Description = "The format of the generated output.",
        DefaultValueFactory = _ => Output.OutputFormat.Yaml,
    };

    public static readonly Option<string?> OutputPath = new("--out")
    {
        Description = "The path the command will write the files to. If omitted, prints output to console.",
    };

    public static readonly Option<string?> TargetFramework = new("--target-framework", "--tfm")
    {
        Description = "Target framework of projects in the solution to search for entities. " +
                      "If omitted, the newest framework is used.",
    };

    public static readonly Option<Regex?> SolutionProjectRegex = new("--project")
    {
        Description = "Regex pattern to filter projects in the solution to search for entities. " +
                      "If omitted, all projects are searched.",
        CustomParser = result =>
        {
            var value = result.Tokens.Single().Value;
            return new Regex(value);
        },
    };

    public static readonly Option<bool> Force = new("--force", "-f")
    {
        Description = "Do not bother the user with questions and just do it.",
        DefaultValueFactory = _ => false,
    };

    public static readonly Option<bool> ClearOutputPath = new("--clear-out")
    {
        Description = "Clear the output path before generating resources.",
        DefaultValueFactory = _ => false,
    };

    public static readonly Option<string> AccessibleDockerImage = new("--docker-image")
    {
        Description = "An accessible docker image to deploy.",
        DefaultValueFactory = _ => "accessible-docker-image",
    };

    public static readonly Option<string> AccessibleDockerTag = new("--docker-image-tag")
    {
        Description = "Tag for an accessible docker image to deploy.",
        DefaultValueFactory = _ => "latest",
    };

    public static readonly Option<bool> NoColor = new("--no-color")
    {
        Description = "Disable colored output.",
        DefaultValueFactory = _ => false,
    };
}
