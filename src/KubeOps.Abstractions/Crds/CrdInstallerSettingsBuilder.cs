// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Crds;

/// <summary>
/// Configures a <see cref="CrdInstallerSettings"/> instance.
/// Set properties directly or use the fluent <c>With*</c> extension methods,
/// then call <see cref="Build"/> to obtain the immutable <see cref="CrdInstallerSettings"/> record.
/// </summary>
public sealed class CrdInstallerSettingsBuilder
{
    /// <summary>
    /// Determines whether existing CRDs should be overwritten.
    /// This is useful for development purposes and should be used with caution.
    /// It is a destructive operation that may lead to data loss.
    /// </summary>
    public bool OverwriteExisting { get; set; }

    /// <summary>
    /// Determines whether the installed CRDs should be deleted when the operator shuts down.
    /// This is a very destructive operation and should only be used in development environments.
    /// </summary>
    public bool DeleteOnShutdown { get; set; }

    /// <summary>
    /// Produces an immutable <see cref="CrdInstallerSettings"/> record from the current configuration.
    /// </summary>
    /// <returns>A fully initialised <see cref="CrdInstallerSettings"/> record.</returns>
    public CrdInstallerSettings Build() => new()
    {
        OverwriteExisting = OverwriteExisting,
        DeleteOnShutdown = DeleteOnShutdown,
    };
}
