// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace KubeOps.Generator.Discovery;

internal static class RegistrationDiscovery
{
    private const string AbstractionsAssemblyName = "KubeOps.Abstractions";

    private const string RegistrationsAttributeName = "KubeOpsGeneratedRegistrationsAttribute";

    private const string RegistrationsAttributeFullName =
        "KubeOps.Abstractions.Builder.KubeOpsGeneratedRegistrationsAttribute";

    // Public key tokens of .NET platform assemblies (BCL, netstandard, ASP.NET Core / extensions,
    // WPF). Platform assemblies never reference user code, so their subgraphs cannot contain
    // marked assemblies and are skipped entirely - the framework closure is by far the largest
    // part of a compilation's reference graph, and the discovery runs on every edit.
    private static readonly HashSet<string> PlatformPublicKeyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "b77a5c561934e089", // mscorlib / System.*
        "b03f5f7f11d50a3a", // System.* / Microsoft.*
        "7cec85d7bea7798e", // System.Private.CoreLib
        "cc7b13ffcd2ddd51", // netstandard
        "adb9793829ddae60", // Microsoft.Extensions.* / ASP.NET Core
        "31bf3856ad364e35", // WPF / WindowsBase
    };

    public static IncrementalValueProvider<EquatableArray<ReferencedRegistrations>> GetReferencedRegistrations(
        IncrementalGeneratorInitializationContext context)
        => context.CompilationProvider.Select(static (compilation, cancellationToken) =>
        {
            var registrations = new List<ReferencedRegistrations>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<IAssemblySymbol>();

            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                queue.Enqueue(assembly);
            }

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var assembly = queue.Dequeue();
                if (!visited.Add(assembly.Identity.GetDisplayName()) || IsPlatformAssembly(assembly))
                {
                    continue;
                }

                // Only assemblies that reference KubeOps.Abstractions can carry the marker
                // attribute (its type lives there), so the attribute decoding is skipped for all
                // others. The check works on plain assembly identities and does not resolve any
                // further symbols.
                if (ReferencesAbstractions(assembly))
                {
                    var attribute = assembly.GetAttributes().FirstOrDefault(a =>
                        a.AttributeClass is { Name: RegistrationsAttributeName } attributeClass
                        && attributeClass.ToDisplayString() == RegistrationsAttributeFullName);

                    if (attribute is not null)
                    {
                        registrations.Add(new ReferencedRegistrations(
                            assembly.Name,
                            GetConstructorArgument(attribute, 0),
                            GetConstructorArgument(attribute, 1)));
                    }
                }

                foreach (var module in assembly.Modules)
                {
                    foreach (var referenced in module.ReferencedAssemblySymbols)
                    {
                        queue.Enqueue(referenced);
                    }
                }
            }

            registrations.Sort((left, right) => string.CompareOrdinal(left.AssemblyName, right.AssemblyName));
            return new EquatableArray<ReferencedRegistrations>(registrations.ToImmutableArray());
        });

    private static bool IsPlatformAssembly(IAssemblySymbol assembly)
    {
        var publicKeyToken = assembly.Identity.PublicKeyToken;
        if (publicKeyToken.IsDefaultOrEmpty)
        {
            return false;
        }

        return PlatformPublicKeyTokens.Contains(
            string.Concat(publicKeyToken.Select(b => b.ToString("x2"))));
    }

    private static bool ReferencesAbstractions(IAssemblySymbol assembly)
        => assembly.Modules.Any(m => m.ReferencedAssemblies.Any(id =>
            string.Equals(id.Name, AbstractionsAssemblyName, StringComparison.OrdinalIgnoreCase)));

    private static string? GetConstructorArgument(AttributeData attribute, int index)
        => attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as string
            : null;
}
