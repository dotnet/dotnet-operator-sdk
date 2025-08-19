// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace KubeOps.Transpiler;

/// <summary>
/// Helper to create <see cref="MetadataLoadContext"/>s.
/// </summary>
public static class ContextCreator
{
    /// <summary>
    /// Create a new <see cref="MetadataLoadContext"/> with the given <paramref name="assemblyPaths"/>
    /// and directly load an assembly into it.
    /// </summary>
    /// <param name="assemblyPaths">A list of paths.</param>
    /// <param name="assembly">The byte array that contains the assembly to load.</param>
    /// <param name="coreAssemblyName">Optional core assembly name.</param>
    /// <returns>The configured <see cref="MetadataLoadContext"/>.</returns>
    public static MetadataLoadContext Create(
        IEnumerable<string> assemblyPaths,
        byte[] assembly,
        string? coreAssemblyName = null)
    {
        var mlc = Create(assemblyPaths, coreAssemblyName);
        mlc.LoadFromByteArray(assembly);
        return mlc;
    }

    /// <summary>
    /// Create a new <see cref="MetadataLoadContext"/> with the given <paramref name="assemblyPaths"/>.
    /// </summary>
    /// <param name="assemblyPaths">A list of paths.</param>
    /// <param name="coreAssemblyName">Optional core assembly name.</param>
    /// <returns>The configured <see cref="MetadataLoadContext"/>.</returns>
    public static MetadataLoadContext Create(IEnumerable<string> assemblyPaths, string? coreAssemblyName = null) =>
        new(new PathAssemblyResolver(assemblyPaths), coreAssemblyName: coreAssemblyName);
}
