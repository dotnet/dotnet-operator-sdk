// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Operator.Constants;

/// <summary>
/// Provides constant values used for caching purposes within the operator.
/// </summary>
public static class CacheConstants
{
    /// <summary>
    /// Contains constant values representing names used within the operator's caching mechanisms.
    /// </summary>
    public static class CacheNames
    {
        /// <summary>
        /// Represents a constant string used as a name for the resource watcher
        /// in the operator's caching mechanisms.
        /// </summary>
        public const string ResourceWatcher = "ResourceWatcher";

        /// <summary>
        /// Cache name used by ResourceWatcher&lt;TEntity&gt; when
        /// <see cref="KubeOps.Abstractions.Builder.ReconcileStrategy.ByResourceVersion"/> is configured.
        /// Having a separate named cache allows independent FusionCache L1/L2 configuration
        /// (e.g. different TTL, distributed backend, or size limits) from the generation-based cache.
        /// </summary>
        public const string ResourceWatcherByResourceVersion = "ResourceWatcher.ByResourceVersion";
    }
}
