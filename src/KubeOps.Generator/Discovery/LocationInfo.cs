// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KubeOps.Generator.Discovery;

// A cache-safe, value-equatable snapshot of a syntax location. Storing Roslyn's Location directly in an
// incremental generator model is unsafe (it holds a SyntaxTree reference and defeats caching), so we
// capture the primitives needed to reconstruct it for diagnostics.
internal record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? CreateFrom(SyntaxNode node)
    {
        var location = node.GetLocation();
        return location.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}
