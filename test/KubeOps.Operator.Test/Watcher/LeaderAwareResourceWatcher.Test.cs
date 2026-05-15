// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Constants;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Moq;

using ZiggyCreatures.Caching.Fusion;

using ILock = k8s.LeaderElection.ILock;

namespace KubeOps.Operator.Test.Watcher;

public sealed class LeaderAwareResourceWatcherTest
{
    [Fact]
    public async Task StoppedLeading_Should_Clear_EntityCache()
    {
        var mockCache = new Mock<IFusionCache>();
        var mockCacheProvider = Mock.Of<IFusionCacheProvider>();
        Mock.Get(mockCacheProvider)
            .Setup(cp => cp.GetCache(It.Is<string>(s => s == CacheConstants.CacheNames.ResourceWatcher)))
            .Returns(mockCache.Object);

        var lifetime = Mock.Of<IHostApplicationLifetime>();
        Mock.Get(lifetime)
            .Setup(l => l.ApplicationStopped)
            .Returns(CancellationToken.None);

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
            lifetime,
            elector,
            Mock.Of<ILogger<LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>(),
            Mock.Of<IKubernetesClient>());
        await watcher.StartAsync(TestContext.Current.CancellationToken);

        // Trigger the private StoppedLeading handler via the testable wrapper.
        watcher.SimulateStoppedLeading();

        mockCache.Verify(c => c.Clear(It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Wraps <see cref="LeaderAwareResourceWatcher{TEntity}"/> to expose the private
    /// <c>StoppedLeading</c> handler for testing, without needing Moq to raise
    /// non-virtual events.
    /// </summary>
    private sealed class TestableLeaderAwareResourceWatcher : LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>
    {
        private readonly k8s.LeaderElection.LeaderElector _elector;

        public TestableLeaderAwareResourceWatcher(
            IFusionCacheProvider cacheProvider,
            IHostApplicationLifetime lifetime,
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
                lifetime,
                elector)
        {
            _elector = elector;
        }

        /// <summary>
        /// Invokes the <c>StoppedLeading</c> callback that was registered on the elector
        /// during construction by retrieving the backing delegate field via reflection.
        /// Direct invocation of <see cref="k8s.LeaderElection.LeaderElector.OnStoppedLeading"/>
        /// is not permitted outside the declaring class, so reflection is the only option.
        /// </summary>
        public void SimulateStoppedLeading()
        {
            var field = typeof(k8s.LeaderElection.LeaderElector)
                .GetField(
                    nameof(k8s.LeaderElection.LeaderElector.OnStoppedLeading),
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);

            var handler = (Action?)field?.GetValue(_elector);
            handler?.Invoke();
        }
    }
}
