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
    public void ReconcileStrategy_Should_Have_Expected_Values()
    {
        Assert.Equal(0, (int)ReconcileStrategy.ByGenerationId);
        Assert.Equal(1, (int)ReconcileStrategy.ByResourceVersion);
    }

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
    public async Task OnEvent_Should_Remove_From_Cache_On_Deleted_Event()
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
    public async Task OnEvent_Should_Enqueue_When_Generation_Changed()
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
    public async Task OnEvent_Should_Skip_Enqueue_When_Generation_Not_Changed()
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
    public async Task OnEvent_Should_Enqueue_When_Entity_Has_DeletionTimestamp_Without_Generation_Check()
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

    private static V1OperatorIntegrationTestEntity CreateTestEntity()
        => new()
        {
            Metadata = new()
            {
                Name = "test-entity",
                NamespaceProperty = "unit-test",
                Uid = Guid.NewGuid().ToString(),
                Generation = 1,
            },
        };

    private static TestableResourceWatcher CreateTestableWatcher(
        IKubernetesClient? kubernetesClient = null,
        IFusionCache? cache = null,
        ITimedEntityQueue<V1OperatorIntegrationTestEntity>? queue = null,
        ILogger<ResourceWatcher<V1OperatorIntegrationTestEntity>>? logger = null,
        bool waitForCancellation = false)
    {
        var activitySource = new ActivitySource("unit-test");
        var settings = new OperatorSettings { Namespace = "unit-test" };
        var kubeClient = kubernetesClient ?? Mock.Of<IKubernetesClient>();
        var cacheProvider = Mock.Of<IFusionCacheProvider>();
        var fCache = cache ?? Mock.Of<IFusionCache>();
        var timedEntityQueue = queue ?? Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        var labelSelector = new DefaultEntityLabelSelector<V1OperatorIntegrationTestEntity>();

        if (waitForCancellation)
        {
            Mock.Get(kubeClient)
                .Setup(client => client.WatchAsync<V1Pod>("unit-test", null, null, true, It.IsAny<CancellationToken>()))
                .Returns<string?, string?, string?, bool?, CancellationToken>((_, _, _, _, cancellationToken) => WaitForCancellationAsync<(WatchEventType, V1Pod)>(cancellationToken));
        }

        Mock.Get(cacheProvider)
            .Setup(cp => cp.GetCache(It.Is<string>(s => s == CacheConstants.CacheNames.ResourceWatcher)))
            .Returns(() => fCache);

        return new(
            activitySource,
            logger ?? Mock.Of<ILogger<ResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            cacheProvider,
            timedEntityQueue,
            settings,
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
