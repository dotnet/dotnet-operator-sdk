// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Help;

namespace KubeOps.Cli.Commands.Generator;

internal static class Generate
{
    public static Command Command
    {
        get
        {
            var cmd = new Command("generate", "Generates elements related to an operator.")
            {
                OperatorGenerator.Command,
            };
            cmd.Aliases.Add("gen");
            cmd.Aliases.Add("g");

            return cmd;
        }
    }
}
