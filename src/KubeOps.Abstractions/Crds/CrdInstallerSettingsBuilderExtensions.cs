// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Crds;

/// <summary>
/// Fluent extension methods for <see cref="CrdInstallerSettingsBuilder"/>.
/// Each method sets one property and returns the same builder instance for chaining.
/// </summary>
public static class CrdInstallerSettingsBuilderExtensions
{
    /// <summary>Sets whether existing CRDs should be overwritten.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="value"><c>true</c> to overwrite existing CRDs; <c>false</c> otherwise.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static CrdInstallerSettingsBuilder WithOverwriteExisting(
        this CrdInstallerSettingsBuilder builder,
        bool value = true)
    {
        builder.OverwriteExisting = value;
        return builder;
    }

    /// <summary>Sets whether installed CRDs should be deleted when the operator shuts down.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="value"><c>true</c> to delete installed CRDs on shutdown; <c>false</c> otherwise.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    public static CrdInstallerSettingsBuilder WithDeleteOnShutdown(
        this CrdInstallerSettingsBuilder builder,
        bool value = true)
    {
        builder.DeleteOnShutdown = value;
        return builder;
    }
}
