// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;

using FluentAssertions;

using k8s;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Constants;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.Logging;

using Moq;

using ZiggyCreatures.Caching.Fusion;

using ILock = k8s.LeaderElection.ILock;

namespace KubeOps.Operator.Test.Watcher;

public sealed class LeaderAwareResourceWatcherTest
{
    [Fact]
    public async Task StoppedLeading_Should_Remove_Only_This_Entity_Types_Cache_Entries()
    {
        var mockCache = new Mock<IFusionCache>();
        var mockCacheProvider = Mock.Of<IFusionCacheProvider>();
        Mock.Get(mockCacheProvider)
            .Setup(cp => cp.GetCache(It.Is<string>(s => s == CacheConstants.CacheNames.ResourceWatcher)))
            .Returns(mockCache.Object);

        var lockMock = new Mock<ILock>();
        lockMock
            .Setup(l => l.GetAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct => { await Task.Delay(Timeout.Infinite, ct); return null!; });

        var elector = new k8s.LeaderElection.LeaderElector(new(lockMock.Object)
        {
            LeaseDuration = TimeSpan.FromSeconds(1),
            RenewDeadline = TimeSpan.FromMilliseconds(500),
            RetryPeriod = TimeSpan.FromMilliseconds(100),
        });

        var watcher = new TestableLeaderAwareResourceWatcher(
            mockCacheProvider,
            elector,
            Mock.Of<ILogger<LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>(),
            Mock.Of<IKubernetesClient>());
        await watcher.StartAsync(TestContext.Current.CancellationToken);

        watcher.SimulateStoppedLeading();

        mockCache.Verify(
            c => c.RemoveByTag(
                It.Is<string>(tag => tag == typeof(V1OperatorIntegrationTestEntity).FullName),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        mockCache.Verify(c => c.Clear(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task StoppedLeading_Stops_Watcher_Even_When_Cache_Cleanup_Throws()
    {
        var mockCache = new Mock<IFusionCache>();
        mockCache
            .Setup(c => c.RemoveByTag(
                It.IsAny<string>(), It.IsAny<FusionCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("tagging disabled"));

        var mockCacheProvider = Mock.Of<IFusionCacheProvider>();
        Mock.Get(mockCacheProvider).Setup(cp => cp.GetCache(It.IsAny<string>())).Returns(mockCache.Object);

        var lockMock = new Mock<ILock>();
        lockMock
            .Setup(l => l.GetAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct => { await Task.Delay(Timeout.Infinite, ct); return null!; });
        var elector = new k8s.LeaderElection.LeaderElector(new(lockMock.Object)
        {
            LeaseDuration = TimeSpan.FromSeconds(1),
            RenewDeadline = TimeSpan.FromMilliseconds(500),
            RetryPeriod = TimeSpan.FromMilliseconds(100),
        });

        var watchStarted = new TaskCompletionSource();
        var watchCancelled = new TaskCompletionSource();
        var clientMock = new Mock<IKubernetesClient>();
        clientMock
            .Setup(c => c.WatchAsync<V1OperatorIntegrationTestEntity>(
                "unit-test", null, null, null, true, It.IsAny<CancellationToken>()))
            .Returns<string?, string?, string?, string?, bool?, CancellationToken>((_, _, _, _, _, ct) =>
                SignalingWatchAsync(watchStarted, watchCancelled, ct));

        var watcher = new TestableLeaderAwareResourceWatcher(
            mockCacheProvider,
            elector,
            Mock.Of<ILogger<LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>(),
            clientMock.Object);

        await watcher.StartAsync(TestContext.Current.CancellationToken);
        watcher.SimulateStartedLeading();
        (await Task.WhenAny(
                watchStarted.Task,
                Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)))
            .Should().Be(watchStarted.Task, "the watch loop should have started");

        watcher.SimulateStoppedLeading();

        (await Task.WhenAny(
                watchCancelled.Task,
                Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)))
            .Should().Be(watchCancelled.Task, "the watch must be stopped even though cache cleanup threw");
        mockCache.Verify(
            c => c.RemoveByTag(
                It.IsAny<string>(), It.IsAny<FusionCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);

        await watcher.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_Stops_Base_Watcher_Even_When_No_Longer_Leader()
    {
        var mockCache = new Mock<IFusionCache>();
        var mockCacheProvider = Mock.Of<IFusionCacheProvider>();
        Mock.Get(mockCacheProvider)
            .Setup(cp => cp.GetCache(It.Is<string>(s => s == CacheConstants.CacheNames.ResourceWatcher)))
            .Returns(mockCache.Object);

        var lockMock = new Mock<ILock>();
        lockMock
            .Setup(l => l.GetAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct => { await Task.Delay(Timeout.Infinite, ct); return null!; });

        var elector = new k8s.LeaderElection.LeaderElector(new(lockMock.Object)
        {
            LeaseDuration = TimeSpan.FromSeconds(1),
            RenewDeadline = TimeSpan.FromMilliseconds(500),
            RetryPeriod = TimeSpan.FromMilliseconds(100),
        });

        var loggerMock = new Mock<ILogger<LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>>>();
        var watcher = new TestableLeaderAwareResourceWatcher(
            mockCacheProvider,
            elector,
            loggerMock.Object,
            Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>(),
            Mock.Of<IKubernetesClient>());

        await watcher.StartAsync(TestContext.Current.CancellationToken);
        await watcher.StopAsync(TestContext.Current.CancellationToken);

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((@object, _) => @object.ToString()!.Contains("Stopping resource watcher")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_Is_Idempotent_And_Starts_Only_One_Watch()
    {
        var mockCacheProvider = Mock.Of<IFusionCacheProvider>();
        Mock.Get(mockCacheProvider).Setup(cp => cp.GetCache(It.IsAny<string>())).Returns(Mock.Of<IFusionCache>());

        var lockMock = new Mock<ILock>();
        lockMock
            .Setup(l => l.GetAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct => { await Task.Delay(Timeout.Infinite, ct); return null!; });
        var elector = new k8s.LeaderElection.LeaderElector(new(lockMock.Object)
        {
            LeaseDuration = TimeSpan.FromSeconds(1),
            RenewDeadline = TimeSpan.FromMilliseconds(500),
            RetryPeriod = TimeSpan.FromMilliseconds(100),
        });

        var watchCallCount = 0;
        var clientMock = new Mock<IKubernetesClient>();
        clientMock
            .Setup(c => c.WatchAsync<V1OperatorIntegrationTestEntity>(
                "unit-test", null, null, null, true, It.IsAny<CancellationToken>()))
            .Returns<string?, string?, string?, string?, bool?, CancellationToken>((_, _, _, _, _, ct) =>
            {
                Interlocked.Increment(ref watchCallCount);
                return WaitForCancellationAsync<(k8s.WatchEventType, V1OperatorIntegrationTestEntity)>(ct);
            });

        var watcher = new TestableLeaderAwareResourceWatcher(
            mockCacheProvider,
            elector,
            Mock.Of<ILogger<LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>(),
            clientMock.Object);

        await watcher.StartAsync(TestContext.Current.CancellationToken);
        watcher.SimulateStartedLeading();
        watcher.SimulateStartedLeading();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        Volatile.Read(ref watchCallCount).Should().Be(1);

        await watcher.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_Unsubscribes_From_Elector()
    {
        var mockCacheProvider = Mock.Of<IFusionCacheProvider>();
        Mock.Get(mockCacheProvider).Setup(cp => cp.GetCache(It.IsAny<string>())).Returns(Mock.Of<IFusionCache>());

        var lockMock = new Mock<ILock>();
        lockMock
            .Setup(l => l.GetAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct => { await Task.Delay(Timeout.Infinite, ct); return null!; });
        var elector = new k8s.LeaderElection.LeaderElector(new(lockMock.Object)
        {
            LeaseDuration = TimeSpan.FromSeconds(1),
            RenewDeadline = TimeSpan.FromMilliseconds(500),
            RetryPeriod = TimeSpan.FromMilliseconds(100),
        });

        var watcher = new TestableLeaderAwareResourceWatcher(
            mockCacheProvider,
            elector,
            Mock.Of<ILogger<LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>(),
            Mock.Of<IKubernetesClient>());

        await watcher.StartAsync(TestContext.Current.CancellationToken); // subscribes the handlers
        GetElectorHandler(elector, nameof(k8s.LeaderElection.LeaderElector.OnStartedLeading)).Should().NotBeNull();

        await watcher.DisposeAsync();

        // The async dispose path must have removed both handlers from the elector.
        GetElectorHandler(elector, nameof(k8s.LeaderElection.LeaderElector.OnStartedLeading)).Should().BeNull();
        GetElectorHandler(elector, nameof(k8s.LeaderElection.LeaderElector.OnStoppedLeading)).Should().BeNull();
    }

    private static Delegate? GetElectorHandler(k8s.LeaderElection.LeaderElector elector, string eventName)
    {
        var field = typeof(k8s.LeaderElection.LeaderElector)
            .GetField(eventName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (Delegate?)field?.GetValue(elector);
    }

    private sealed class TestableLeaderAwareResourceWatcher : LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>
    {
        private readonly k8s.LeaderElection.LeaderElector _elector;

        public TestableLeaderAwareResourceWatcher(
            IFusionCacheProvider cacheProvider,
            k8s.LeaderElection.LeaderElector elector,
            ILogger<LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>> logger,
            ITimedEntityQueue<V1OperatorIntegrationTestEntity> queue,
            IKubernetesClient client)
            : base(
                new("test"),
                logger,
                cacheProvider,
                queue,
                new OperatorSettingsBuilder { Namespace = "unit-test" }.Build(),
                new DefaultEntityLabelSelector<V1OperatorIntegrationTestEntity>(),
                new DefaultEntityFieldSelector<V1OperatorIntegrationTestEntity>(),
                client,
                elector,
                Mock.Of<IEntityLoggingScopeFactory<V1OperatorIntegrationTestEntity>>())
        {
            _elector = elector;
        }

        /// <summary>
        /// Invokes the <c>StoppedLeading</c> callback that was registered on the elector
        /// during construction by retrieving the backing delegate field via reflection.
        /// Direct invocation of <see cref="k8s.LeaderElection.LeaderElector.OnStoppedLeading"/>
        /// is not permitted outside the declaring class, so reflection is the only option.
        /// </summary>
        public void SimulateStoppedLeading() => InvokeElectorEvent(nameof(k8s.LeaderElection.LeaderElector.OnStoppedLeading));

        public void SimulateStartedLeading() => InvokeElectorEvent(nameof(k8s.LeaderElection.LeaderElector.OnStartedLeading));

        private void InvokeElectorEvent(string eventName)
        {
            var field = typeof(k8s.LeaderElection.LeaderElector)
                .GetField(eventName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            var handler = (Action?)field?.GetValue(_elector);
            handler?.Invoke();
        }
    }

    private static async IAsyncEnumerable<T> WaitForCancellationAsync<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    private static async IAsyncEnumerable<(WatchEventType, V1OperatorIntegrationTestEntity)> SignalingWatchAsync(
        TaskCompletionSource started,
        TaskCompletionSource cancelled,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        started.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            cancelled.TrySetResult();
        }

        yield break;
    }
}
