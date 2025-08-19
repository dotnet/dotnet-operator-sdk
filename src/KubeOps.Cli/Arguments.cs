// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;

namespace KubeOps.Cli;

internal static class Arguments
{
    public static readonly Argument<FileInfo> SolutionOrProjectFile = new("sln/csproj file")
    {
        DefaultValueFactory = result =>
        {
            var projectFile
                = Directory.EnumerateFiles(
                        Directory.GetCurrentDirectory(),
                        "*.csproj")
                    .Select(f => new FileInfo(f))
                    .FirstOrDefault();
            var slnFile
                = Directory.EnumerateFiles(
                        Directory.GetCurrentDirectory(),
                        "*.sln")
                    .Select(f => new FileInfo(f))
                    .FirstOrDefault();
            var file = (projectFile, slnFile) switch
            {
                ({ } prj, _) => prj,
                (_, { } sln) => sln,
                _ => null,
            };

            if (file is not null)
            {
                return file;
            }

            result.AddError("No solution or project file found in the current directory, and none was provided.");
            return new FileInfo("not-found");
        },
        Description = "A solution or project file where entities are located. " +
                      "If omitted, the current directory is searched for a *.csproj or *.sln file. " +
                      "If an *.sln file is used, all projects in the solution (with the newest framework) will be searched for entities. " +
                      "This behaviour can be filtered by using the --project and --target-framework option.",
    };

    public static readonly Argument<string> OperatorName = new("name") { Description = "Name of the operator.", };
}
