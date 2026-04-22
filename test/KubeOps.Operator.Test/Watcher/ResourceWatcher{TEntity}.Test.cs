// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Constants;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.Logging;

using Moq;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Test.Watcher;

public sealed class ResourceWatcherTest
{
    [Fact]
    public async Task Restarting_Watcher_Should_Trigger_New_Watch()
    {
        // Arrange
        var kubernetesClient = Mock.Of<IKubernetesClient>();
        var resourceWatcher = CreateTestableWatcher(kubernetesClient, waitForCancellation: true);

        // Act
        // Start and stop the watcher
        await resourceWatcher.StartAsync(TestContext.Current.CancellationToken);
        await resourceWatcher.StopAsync(TestContext.Current.CancellationToken);

        // Restart the watcher
        await resourceWatcher.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Mock.Get(kubernetesClient)
            .Verify(client => client.WatchAsync<V1OperatorIntegrationTestEntity>(
                    "unit-test",
                    null,
                    null,
                    true,
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
    }

    [Fact]
    public async Task OnEvent_Should_Remove_From_Cache_On_Deleted_When_Strategy_Is_ByGeneration()
    {
        // Arrange
        var entity = CreateTestEntity();
        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object);

        // Act
        await watcher.InvokeOnEventAsync(
            WatchEventType.Deleted,
            entity,
            TestContext.Current.CancellationToken);

        // Assert
        mockCache.Verify(
            c => c.RemoveAsync(
                It.Is<string>(uuid => uuid == entity.Uid()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        mockCache.Verify(
            c => c.SetAsync(
                It.Is<string>(uuid => uuid == entity.Uid()),
                It.IsAny<long>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnEvent_Should_Throw_When_ReconcileStrategy_Is_Unknown()
    {
        // Arrange
        var entity = CreateTestEntity();
        var settings = new OperatorSettingsBuilder { Namespace = "unit-test", ReconcileStrategy = (ReconcileStrategy)99 }.Build();
        var watcher = CreateTestableWatcher(settings: settings);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            watcher.InvokeOnEventAsync(
                WatchEventType.Modified,
                entity,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnEvent_Should_Enqueue_When_Generation_Changed_And_Strategy_Is_ByGeneration()
    {
        // Arrange
        var entity = CreateTestEntity();
        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object);

        mockCache
            .Setup(c =>
                c.TryGetAsync<long>(
                    It.Is<string>(s => s == entity.Uid()),
                    It.IsAny<FusionCacheEntryOptions>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(MaybeValue<long>.FromValue(entity.Generation()!.Value - 1));

        // Act
        await watcher
            .InvokeOnEventAsync(
                WatchEventType.Modified,
                entity,
                TestContext.Current.CancellationToken);

        // Assert
        mockQueue.Verify(
            q => q.Enqueue(
                    entity,
                    ReconciliationType.Modified,
                    ReconciliationTriggerSource.ApiServer,
                    TimeSpan.Zero,
                    0,
                    It.IsAny<CancellationToken>()),
            Times.Once);
        mockCache.Verify(
            c => c.SetAsync(
                It.Is<string>(uuid => uuid == entity.Uid()),
                It.Is<long>(generation => generation == entity.Generation()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnEvent_Should_Enqueue_When_Cache_Is_Empty_And_Strategy_Is_ByGeneration()
    {
        // Arrange – no cache entry (first event after start or after leader failover)
        var entity = CreateTestEntity();
        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object);

        mockCache
            .Setup(c => c.TryGetAsync<long>(
                It.Is<string>(s => s == entity.Uid()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MaybeValue<long>.None);

        // Act
        await watcher.InvokeOnEventAsync(WatchEventType.Modified, entity, TestContext.Current.CancellationToken);

        // Assert – enqueued because cache had no entry
        mockQueue.Verify(
            q => q.Enqueue(
                entity,
                ReconciliationType.Modified,
                ReconciliationTriggerSource.ApiServer,
                TimeSpan.Zero,
                0,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnEvent_Should_Enqueue_When_Cache_Is_Empty_And_Strategy_Is_ByResourceVersion()
    {
        // Arrange – no cache entry (first event after start or after leader failover)
        var entity = CreateTestEntity();
        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var settings = new OperatorSettingsBuilder { Namespace = "unit-test", ReconcileStrategy = ReconcileStrategy.ByResourceVersion }.Build();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object, settings: settings);

        mockCache
            .Setup(c => c.TryGetAsync<string>(
                It.Is<string>(s => s == entity.Uid()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MaybeValue<string>.None);

        // Act
        await watcher.InvokeOnEventAsync(WatchEventType.Modified, entity, TestContext.Current.CancellationToken);

        // Assert – enqueued because cache had no entry
        mockQueue.Verify(
            q => q.Enqueue(
                entity,
                ReconciliationType.Modified,
                ReconciliationTriggerSource.ApiServer,
                TimeSpan.Zero,
                0,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnEvent_Should_Skip_Enqueue_When_Generation_Unchanged_And_Strategy_Is_ByGeneration()
    {
        // Arrange
        var entity = CreateTestEntity();
        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var mockLogger = new Mock<ILogger<ResourceWatcher<V1OperatorIntegrationTestEntity>>>();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object, logger: mockLogger.Object);

        mockCache
            .Setup(c => c.TryGetAsync<long>(
                It.Is<string>(s => s == entity.Uid()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MaybeValue<long>.FromValue(entity.Generation()!.Value));

        // Act
        await watcher
            .InvokeOnEventAsync(
                WatchEventType.Added,
                entity,
                TestContext.Current.CancellationToken);

        // Assert
        mockQueue.Verify(
            q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(),
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        mockCache.Verify(
            c => c.SetAsync(
                It.Is<string>(uuid => uuid == entity.Uid()),
                It.IsAny<long>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        mockLogger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Debug),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) => @object.ToString() == $"""Entity "{entity.ToIdentifierString()}" modification did not modify generation. Skip event.""" && type.Name == "FormattedLogValues"),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Fact]
    public void Constructor_Should_Request_ResourceWatcher_Cache_For_ByGeneration_Strategy()
    {
        var mockCacheProvider = new Mock<IFusionCacheProvider>();
        mockCacheProvider
            .Setup(cp => cp.GetCache(It.IsAny<string>()))
            .Returns(Mock.Of<IFusionCache>());

        _ = CreateTestableWatcher(
            cacheProvider: mockCacheProvider.Object,
            settings: new OperatorSettingsBuilder { Namespace = "unit-test", ReconcileStrategy = ReconcileStrategy.ByGeneration }.Build());

        mockCacheProvider.Verify(
            cp => cp.GetCache(It.Is<string>(s => s == CacheConstants.CacheNames.ResourceWatcher)),
            Times.Once);
        mockCacheProvider.Verify(
            cp => cp.GetCache(It.Is<string>(s => s == CacheConstants.CacheNames.ResourceWatcherByResourceVersion)),
            Times.Never);
    }

    [Fact]
    public void Constructor_Should_Request_ResourceWatcherByResourceVersion_Cache_For_ByResourceVersion_Strategy()
    {
        var mockCacheProvider = new Mock<IFusionCacheProvider>();
        mockCacheProvider
            .Setup(cp => cp.GetCache(It.IsAny<string>()))
            .Returns(Mock.Of<IFusionCache>());

        _ = CreateTestableWatcher(
            cacheProvider: mockCacheProvider.Object,
            settings: new OperatorSettingsBuilder { Namespace = "unit-test", ReconcileStrategy = ReconcileStrategy.ByResourceVersion }.Build());

        mockCacheProvider.Verify(
            cp => cp.GetCache(It.Is<string>(s => s == CacheConstants.CacheNames.ResourceWatcherByResourceVersion)),
            Times.Once);
        mockCacheProvider.Verify(
            cp => cp.GetCache(It.Is<string>(s => s == CacheConstants.CacheNames.ResourceWatcher)),
            Times.Never);
    }

    [Fact]
    public async Task OnEvent_Should_Enqueue_When_Entity_Has_DeletionTimestamp_And_Strategy_Is_ByGeneration()
    {
        // Arrange
        var entity = CreateTestEntity();
        entity.Metadata.DeletionTimestamp = DateTime.UtcNow;

        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object);

        // Act
        await watcher.InvokeOnEventAsync(
            WatchEventType.Modified,
            entity,
            TestContext.Current.CancellationToken);

        // Assert – enqueued without any cache read (generation check bypassed)
        mockQueue.Verify(
            q => q.Enqueue(
                entity,
                ReconciliationType.Modified,
                ReconciliationTriggerSource.ApiServer,
                TimeSpan.Zero,
                0,
                It.IsAny<CancellationToken>()),
            Times.Once);
        mockCache.Verify(
            c => c.TryGetAsync<long>(
                It.IsAny<string>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnEvent_Should_Enqueue_When_ResourceVersion_Changed_And_Strategy_Is_ByResourceVersion()
    {
        // Arrange – cache holds old resourceVersion "1", entity now has "2"
        var entity = CreateTestEntity(resourceVersion: "2");
        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var settings = new OperatorSettingsBuilder { Namespace = "unit-test", ReconcileStrategy = ReconcileStrategy.ByResourceVersion }.Build();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object, settings: settings);

        mockCache
            .Setup(c => c.TryGetAsync<string>(
                It.Is<string>(s => s == entity.Uid()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MaybeValue<string>.FromValue("1"));

        // Act
        await watcher.InvokeOnEventAsync(WatchEventType.Modified, entity, TestContext.Current.CancellationToken);

        // Assert – enqueued and cache updated to new resourceVersion
        mockQueue.Verify(
            q => q.Enqueue(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer, TimeSpan.Zero, 0, It.IsAny<CancellationToken>()),
            Times.Once);
        mockCache.Verify(
            c => c.SetAsync(
                It.Is<string>(uid => uid == entity.Uid()),
                It.Is<string>(rv => rv == "2"),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnEvent_Should_Skip_Enqueue_When_ResourceVersion_Not_Changed_And_Strategy_Is_ByResourceVersion()
    {
        // Arrange – cache holds same resourceVersion as entity
        var entity = CreateTestEntity(resourceVersion: "5");
        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var mockLogger = new Mock<ILogger<ResourceWatcher<V1OperatorIntegrationTestEntity>>>();
        var settings = new OperatorSettingsBuilder { Namespace = "unit-test", ReconcileStrategy = ReconcileStrategy.ByResourceVersion }.Build();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object, logger: mockLogger.Object, settings: settings);

        mockCache
            .Setup(c => c.TryGetAsync<string>(
                It.Is<string>(s => s == entity.Uid()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MaybeValue<string>.FromValue("5"));

        // Act
        await watcher.InvokeOnEventAsync(WatchEventType.Modified, entity, TestContext.Current.CancellationToken);

        // Assert – not enqueued
        mockQueue.Verify(
            q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(), It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(), It.IsAny<TimeSpan>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mockLogger.Verify(logger => logger.Log(
                It.Is<LogLevel>(l => l == LogLevel.Debug),
                It.Is<EventId>(e => e.Id == 0),
                It.Is<It.IsAnyType>((@object, type) =>
                    @object.ToString() == $"""Entity "{entity.ToIdentifierString()}" resourceVersion unchanged. Skip event."""
                    && type.Name == "FormattedLogValues"),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Fact]
    public async Task OnEvent_Should_Enqueue_On_Entity_With_DeletionTimestamp_When_Strategy_Is_ByResourceVersion()
    {
        // Arrange – entity undergoing finalizer processing (DeletionTimestamp set, new resourceVersion)
        var entity = CreateTestEntity(resourceVersion: "10");
        entity.Metadata.DeletionTimestamp = DateTime.UtcNow;
        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var settings = new OperatorSettingsBuilder { Namespace = "unit-test", ReconcileStrategy = ReconcileStrategy.ByResourceVersion }.Build();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object, settings: settings);

        mockCache
            .Setup(c => c.TryGetAsync<string>(
                It.Is<string>(s => s == entity.Uid()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MaybeValue<string>.FromValue("9"));

        // Act
        await watcher.InvokeOnEventAsync(WatchEventType.Modified, entity, TestContext.Current.CancellationToken);

        // Assert – enqueued because resourceVersion changed (finalizer removal is a real write)
        mockQueue.Verify(
            q => q.Enqueue(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer, TimeSpan.Zero, 0, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnEvent_Should_Remove_From_Cache_On_Deleted_When_Strategy_Is_ByResourceVersion()
    {
        // Arrange
        var entity = CreateTestEntity(resourceVersion: "7");
        var mockCache = new Mock<IFusionCache>();
        var mockQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var settings = new OperatorSettingsBuilder { Namespace = "unit-test", ReconcileStrategy = ReconcileStrategy.ByResourceVersion }.Build();
        var watcher = CreateTestableWatcher(cache: mockCache.Object, queue: mockQueue.Object, settings: settings);

        // Act
        await watcher.InvokeOnEventAsync(WatchEventType.Deleted, entity, TestContext.Current.CancellationToken);

        // Assert – cache entry removed and entity enqueued for deletion reconciliation
        mockCache.Verify(
            c => c.RemoveAsync(
                It.Is<string>(uid => uid == entity.Uid()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        mockQueue.Verify(
            q => q.Enqueue(entity, ReconciliationType.Deleted, ReconciliationTriggerSource.ApiServer, TimeSpan.Zero, 0, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static V1OperatorIntegrationTestEntity CreateTestEntity(string resourceVersion = "1")
        => new()
        {
            Metadata = new()
            {
                Name = "test-entity",
                NamespaceProperty = "unit-test",
                Uid = Guid.NewGuid().ToString(),
                Generation = 1,
                ResourceVersion = resourceVersion,
            },
        };

    private static TestableResourceWatcher CreateTestableWatcher(
        IKubernetesClient? kubernetesClient = null,
        IFusionCache? cache = null,
        IFusionCacheProvider? cacheProvider = null,
        ITimedEntityQueue<V1OperatorIntegrationTestEntity>? queue = null,
        ILogger<ResourceWatcher<V1OperatorIntegrationTestEntity>>? logger = null,
        OperatorSettings? settings = null,
        bool waitForCancellation = false)
    {
        var activitySource = new ActivitySource("unit-test");
        var effectiveSettings = settings ?? new OperatorSettingsBuilder { Namespace = "unit-test" }.Build();
        var kubeClient = kubernetesClient ?? Mock.Of<IKubernetesClient>();
        var fCache = cache ?? Mock.Of<IFusionCache>();
        var timedEntityQueue = queue ?? Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var labelSelector = new DefaultEntityLabelSelector<V1OperatorIntegrationTestEntity>();

        // If a fully configured cacheProvider is passed, use it directly.
        // Otherwise build a default mock that returns fCache for any cache name.
        var effectiveCacheProvider = cacheProvider ?? Mock.Of<IFusionCacheProvider>();
        if (cacheProvider is null)
        {
            Mock.Get(effectiveCacheProvider)
                .Setup(cp => cp.GetCache(It.IsAny<string>()))
                .Returns(() => fCache);
        }

        if (waitForCancellation)
        {
            Mock.Get(kubeClient)
                .Setup(client => client.WatchAsync<V1Pod>("unit-test", null, null, true, It.IsAny<CancellationToken>()))
                .Returns<string?, string?, string?, bool?, CancellationToken>((_, _, _, _, cancellationToken) => WaitForCancellationAsync<(WatchEventType, V1Pod)>(cancellationToken));
        }

        return new(
            activitySource,
            logger ?? Mock.Of<ILogger<ResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            effectiveCacheProvider,
            timedEntityQueue,
            effectiveSettings,
            labelSelector,
            kubeClient);
    }

    private static async IAsyncEnumerable<T> WaitForCancellationAsync<T>([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield return default!;
    }

    private sealed class TestableResourceWatcher(
        ActivitySource activitySource,
        ILogger<ResourceWatcher<V1OperatorIntegrationTestEntity>> logger,
        IFusionCacheProvider cacheProvider,
        ITimedEntityQueue<V1OperatorIntegrationTestEntity> queue,
        OperatorSettings settings,
        IEntityLabelSelector<V1OperatorIntegrationTestEntity> labelSelector,
        IKubernetesClient client)
        : ResourceWatcher<V1OperatorIntegrationTestEntity>(activitySource, logger, cacheProvider, queue, settings, labelSelector, client)
    {
        public Task InvokeOnEventAsync(WatchEventType eventType, V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => OnEventAsync(eventType, entity, cancellationToken);
    }
}
