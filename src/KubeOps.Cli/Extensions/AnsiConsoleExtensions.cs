// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.CommandLine;

using Spectre.Console;

namespace KubeOps.Cli.Extensions;

/// <summary>
/// Provides extension methods for working with ANSI console.
/// </summary>
public static class AnsiConsoleExtensions
{
    /// <summary>
    /// Applies options from the provided parse result to configure the ANSI console behavior.
    /// </summary>
    /// <param name="console">The ANSI console instance to configure.</param>
    /// <param name="parseResult">The parsed command-line arguments containing configuration options.</param>
#pragma warning disable RCS1175
    public static void ApplyOptions(this IAnsiConsole console, ParseResult parseResult)
#pragma warning restore RCS1175
    {
        var noAnsi = parseResult.GetValue(Options.NoAnsi);

        if (!noAnsi)
        {
            return;
        }

        AnsiConsole.Console.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        AnsiConsole.Console.Profile.Capabilities.Ansi = false;
    }
}
