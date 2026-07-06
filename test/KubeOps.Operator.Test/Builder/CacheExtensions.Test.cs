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
        services.WithResourceWatcherEntityCaching(new OperatorSettingsBuilder { ReconcileStrategy = strategy }.Build());
        var provider = services.BuildServiceProvider();

        var cacheProvider = provider.GetRequiredService<IFusionCacheProvider>();
        var cache = cacheProvider.GetCache(expectedCacheName);

        cache.Should().NotBeNull();
    }

    [Fact]
    public void WithResourceWatcherEntityCaching_Should_Invoke_Custom_Cache_Configurator_When_Provided()
    {
        var configuratorInvoked = false;
        var settings = new OperatorSettingsBuilder
        {
            ConfigureResourceWatcherEntityCache = _ => configuratorInvoked = true,
        }.Build();

        new ServiceCollection().WithResourceWatcherEntityCaching(settings);

        configuratorInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Default_ResourceWatcher_Cache_Supports_Per_Entity_Tag_Removal()
    {
        // The leadership-aware watcher relies on FusionCache tagging: each dedup entry is tagged with its entity
        // type, and on leadership loss only that type's entries are dropped via RemoveByTag. This verifies the
        // default cache registration actually supports tagging at runtime (the unit tests elsewhere mock the cache),
        // and that a tag removal affects only the matching entries.
        var services = new ServiceCollection();
        services.WithResourceWatcherEntityCaching(
            new OperatorSettingsBuilder { ReconcileStrategy = ReconcileStrategy.ByGeneration }.Build());
        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IFusionCacheProvider>()
            .GetCache(CacheConstants.CacheNames.ResourceWatcher);

        var token = TestContext.Current.CancellationToken;
        await cache.SetAsync("uid-a", 1L, tags: ["EntityA"], token: token);
        await cache.SetAsync("uid-b", 2L, tags: ["EntityB"], token: token);

        await cache.RemoveByTagAsync("EntityA", token: token);

        (await cache.TryGetAsync<long>("uid-a", token: token)).HasValue
            .Should().BeFalse("EntityA's tagged entry must be removed");
        var remaining = await cache.TryGetAsync<long>("uid-b", token: token);
        remaining.HasValue.Should().BeTrue("EntityB's entry shares the cache but a different tag");
        remaining.Value.Should().Be(2L);
    }
}
