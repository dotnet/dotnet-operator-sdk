// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Builder;
using KubeOps.Operator.Constants;

using Microsoft.Extensions.DependencyInjection;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Builder;

/// <summary>
/// Provides extension methods for configuring caching related to the operator.
/// </summary>
internal static class CacheExtensions
{
    /// <summary>
    /// Configures resource watcher caching for the given service collection.
    /// Adds a FusionCache instance for resource watchers and applies custom or default cache configuration.
    /// </summary>
    /// <param name="services">The service collection to add the resource watcher caching to.</param>
    /// <param name="settings">
    /// The operator settings that optionally provide a custom configuration for the resource watcher entity cache.
    /// </param>
    /// <returns>The modified service collection with resource watcher caching configured.</returns>
    internal static IServiceCollection WithResourceWatcherEntityCaching(this IServiceCollection services, OperatorSettings settings)
    {
        var cacheBuilder = services
            .AddFusionCache(CacheConstants.CacheNames.ResourceWatcher);

        if (settings.ConfigureResourceWatcherEntityCache != default)
        {
            settings.ConfigureResourceWatcherEntityCache(cacheBuilder);
        }
        else
        {
            cacheBuilder
                .WithOptions(options =>
                {
                    options.CacheKeyPrefix = $"{CacheConstants.CacheNames.ResourceWatcher}:";
                    options.DefaultEntryOptions
                        .SetDuration(TimeSpan.MaxValue);
                });
        }

        return services;
    }
}
