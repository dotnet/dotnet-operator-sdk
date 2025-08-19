// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;

namespace KubeOps.Cli.Transpilation;

/// <summary>
/// Tfm Comparer.
/// </summary>
internal sealed partial class TfmComparer : IComparer<string>
{
    [GeneratedRegex(
        "[(]?(?<tfm>(?<n>(netcoreapp|net|netstandard){1})(?<major>[0-9]+)[.](?<minor>[0-9]+))[)]?",
        RegexOptions.Compiled)]
    public static partial Regex TfmRegex();

    public int Compare(string? x, string? y)
    {
        if (x == null || y == null)
        {
            return StringComparer.CurrentCulture.Compare(x, y);
        }

        switch (TfmRegex().Match(x), TfmRegex().Match(y))
        {
            case ({ Success: false }, _) or (_, { Success: false }):
                return StringComparer.CurrentCulture.Compare(x, y);
            case ({ } matchX, { } matchY):
                var platformX = matchX.Groups["name"].Value;
                var platformY = matchY.Groups["name"].Value;
                if (platformX != platformY)
                {
                    return (platformX, platformY) switch
                    {
                        ("netstandard", _) or (_, "net") => -1,
                        (_, "netstandard") or ("net", _) => 1,
                        _ => 0,
                    };
                }

                var majorX = matchX.Groups["major"].Value;
                var majorY = matchY.Groups["major"].Value;
                if (majorX != majorY)
                {
                    return int.Parse(majorX) - int.Parse(majorY);
                }

                var minorX = matchX.Groups["minor"].Value;
                var minorY = matchY.Groups["minor"].Value;
                if (minorX != minorY)
                {
                    return int.Parse(minorX) - int.Parse(minorY);
                }

                return 0;
            default:
                return 0;
        }
    }
}
