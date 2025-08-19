// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;

namespace KubeOps.Transpiler.Kubernetes;

/// <summary>
/// Comparer for Kubernetes Versions. Uses the version priority according to
/// <a href="https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definition-versioning/#version-priority">
/// the Kubernetes documentation
/// </a>.
/// </summary>
public sealed partial class KubernetesVersionComparer : IComparer<string>
{
    private enum Stream
    {
        Alpha = 1,
        Beta = 2,
        Final = 3,
    }

    public int Compare(string? x, string? y)
    {
        if (x == null || y == null)
        {
            return StringComparer.CurrentCulture.Compare(x, y);
        }

        var matchX = KubernetesVersionRegex().Match(x);
        if (!matchX.Success)
        {
            return StringComparer.CurrentCulture.Compare(x, y);
        }

        var matchY = KubernetesVersionRegex().Match(y);
        if (!matchY.Success)
        {
            return StringComparer.CurrentCulture.Compare(x, y);
        }

        var versionX = ExtractVersion(matchX);
        var versionY = ExtractVersion(matchY);
        return versionX.CompareTo(versionY);
    }

    [GeneratedRegex("^v(?<major>[0-9]+)((?<stream>alpha|beta)(?<minor>[0-9]+))?$", RegexOptions.Compiled)]
    private static partial Regex KubernetesVersionRegex();

    private static Version ExtractVersion(Match match)
    {
        var major = int.Parse(match.Groups["major"].Value);
        if (!Enum.TryParse<Stream>(match.Groups["stream"].Value, true, out var stream))
        {
            stream = Stream.Final;
        }

        _ = int.TryParse(match.Groups["minor"].Value, out var minor);
        return new Version(major, (int)stream, minor);
    }
}
