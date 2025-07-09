namespace KubeOps.Abstractions.Crds;

/// <summary>
/// Settings for the CRD installer.
/// </summary>
public sealed class CrdInstallerSettings
{
    /// <summary>
    /// Determines whether existing CRDs should be overwritten.
    /// This is useful for development purposes and should be used with caution.
    /// It is a destructive operation that may lead to data loss.
    /// </summary>
    public bool OverwriteExisting { get; set; } = false;

    /// <summary>
    /// Determines whether the installed CRDs should be deleted when the operator shuts down.
    /// This is a very destructive operation and should only be used in development environments.
    /// </summary>
    public bool DeleteOnShutdown { get; set; } = false;
}
