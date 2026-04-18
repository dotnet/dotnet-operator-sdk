// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Constants;

using Microsoft.Extensions.DependencyInjection;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Test.Builder;

public sealed class CacheExtensionsTest
{
    [Theory]
    [InlineData(ReconcileStrategy.ByGeneration, CacheConstants.CacheNames.ResourceWatcher)]
    [InlineData(ReconcileStrategy.ByResourceVersion, CacheConstants.CacheNames.ResourceWatcherByResourceVersion)]
    public void WithResourceWatcherEntityCaching_Should_Register_Resolvable_Named_Cache_For_Strategy(
        ReconcileStrategy strategy,
        string expectedCacheName)
    {
        var services = new ServiceCollection();
        services.WithResourceWatcherEntityCaching(new() { ReconcileStrategy = strategy });
        var provider = services.BuildServiceProvider();

        var cacheProvider = provider.GetRequiredService<IFusionCacheProvider>();
        var cache = cacheProvider.GetCache(expectedCacheName);

        cache.Should().NotBeNull();
    }

    [Fact]
    public void WithResourceWatcherEntityCaching_Should_Invoke_Custom_Cache_Configurator_When_Provided()
    {
        var configuratorInvoked = false;
        var settings = new OperatorSettings
        {
            ConfigureResourceWatcherEntityCache = _ => configuratorInvoked = true,
        };

        new ServiceCollection().WithResourceWatcherEntityCaching(settings);

        configuratorInvoked.Should().BeTrue();
    }
}
