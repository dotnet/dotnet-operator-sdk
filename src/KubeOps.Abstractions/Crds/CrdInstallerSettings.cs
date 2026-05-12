// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Crds;

/// <summary>
/// Immutable settings for the CRD installer. Created via <see cref="CrdInstallerSettingsBuilder.Build"/>.
/// </summary>
public sealed record CrdInstallerSettings
{
    /// <summary>
    /// Determines whether existing CRDs should be overwritten.
    /// This is useful for development purposes and should be used with caution.
    /// It is a destructive operation that may lead to data loss.
    /// </summary>
    public required bool OverwriteExisting { get; init; }

    /// <summary>
    /// Determines whether the installed CRDs should be deleted when the operator shuts down.
    /// This is a very destructive operation and should only be used in development environments.
    /// </summary>
    public required bool DeleteOnShutdown { get; init; }
}
