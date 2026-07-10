// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.LeaderElection;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.Logging;

using Moq;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Test.Watcher;

[Trait("Area", "ScopedLeaderElection")]
public sealed class ScopeAwareResourceWatcherTest
{
    private readonly Mock<ILeadershipScope> _scope = new();
    private readonly Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> _queue = new();
    private readonly Mock<IKubernetesClient> _client = new();

    public ScopeAwareResourceWatcherTest()
    {
        _queue
            .Setup(q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(),
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Should_Skip_Event_When_Not_Responsible_For_Namespace()
    {
        _scope
            .Setup(s => s.IsResponsibleForAsync("foreign-namespace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        using var watcher = CreateWatcher();

        await watcher.InvokeOnEventAsync(
            WatchEventType.Modified,
            CreateEntity("foreign-namespace"),
            TestContext.Current.CancellationToken);

        _queue.Verify(
            q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(),
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_Enqueue_Event_When_Responsible_For_Namespace()
    {
        _scope
            .Setup(s => s.IsResponsibleForAsync("owned-namespace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var watcher = CreateWatcher();

        await watcher.InvokeOnEventAsync(
            WatchEventType.Modified,
            CreateEntity("owned-namespace"),
            TestContext.Current.CancellationToken);

        _queue.Verify(
            q => q.Enqueue(
                It.Is<V1OperatorIntegrationTestEntity>(e => e.Namespace() == "owned-namespace"),
                ReconciliationType.Modified,
                ReconciliationTriggerSource.ApiServer,
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_Resync_Acquired_Namespace_Through_Regular_Event_Path()
    {
        var entity = CreateEntity("acquired-namespace");
        var enqueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scope
            .Setup(s => s.IsResponsibleForAsync("acquired-namespace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _client
            .Setup(c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                "acquired-namespace",
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([entity]);
        _queue
            .Setup(q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(),
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() => enqueued.TrySetResult());
        using var watcher = CreateWatcher();
        await watcher.StartAsync(TestContext.Current.CancellationToken);

        _scope.Raise(
            s => s.ScopeChanged += null,
            new LeadershipScopeChange(["acquired-namespace"], []));

        await enqueued.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        _queue.Verify(
            q => q.Enqueue(
                It.Is<V1OperatorIntegrationTestEntity>(e => e.Namespace() == "acquired-namespace"),
                ReconciliationType.Modified,
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        await watcher.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Not_Resync_Namespace_Outside_Of_Watched_Namespace()
    {
        using var watcher = CreateWatcher(new OperatorSettingsBuilder { Namespace = "watched-namespace" }.Build());
        await watcher.StartAsync(TestContext.Current.CancellationToken);

        _scope.Raise(
            s => s.ScopeChanged += null,
            new LeadershipScopeChange(["other-namespace"], []));

        // The resync runs fire-and-forget; give it a moment before verifying nothing was listed.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        _client.Verify(
            c => c.ListAsync<V1OperatorIntegrationTestEntity>(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        await watcher.StopAsync(TestContext.Current.CancellationToken);
    }

    private static V1OperatorIntegrationTestEntity CreateEntity(string @namespace)
        => new()
        {
            Metadata = new V1ObjectMeta { Name = "test-entity", NamespaceProperty = @namespace, Uid = "uid-1" },
        };

    private TestableWatcher CreateWatcher(OperatorSettings? settings = null)
    {
        var cacheProvider = Mock.Of<IFusionCacheProvider>();
        Mock.Get(cacheProvider)
            .Setup(cp => cp.GetCache(It.IsAny<string>()))
            .Returns(Mock.Of<IFusionCache>());

        var labelSelector = new Mock<IEntityLabelSelector<V1OperatorIntegrationTestEntity>>();
        labelSelector
            .Setup(s => s.GetLabelSelectorAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<string?>(null));
        var fieldSelector = new Mock<IEntityFieldSelector<V1OperatorIntegrationTestEntity>>();
        fieldSelector
            .Setup(s => s.GetFieldSelectorAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<string?>(null));

        return new TestableWatcher(
            cacheProvider,
            _queue.Object,
            settings ?? new OperatorSettingsBuilder().Build(),
            labelSelector.Object,
            fieldSelector.Object,
            _client.Object,
            _scope.Object);
    }

    private sealed class TestableWatcher(
        IFusionCacheProvider cacheProvider,
        ITimedEntityQueue<V1OperatorIntegrationTestEntity> queue,
        OperatorSettings settings,
        IEntityLabelSelector<V1OperatorIntegrationTestEntity> labelSelector,
        IEntityFieldSelector<V1OperatorIntegrationTestEntity> fieldSelector,
        IKubernetesClient client,
        ILeadershipScope leadershipScope)
        : ScopeAwareResourceWatcher<V1OperatorIntegrationTestEntity>(
            new ActivitySource("test"),
            Mock.Of<ILogger<ScopeAwareResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            cacheProvider,
            queue,
            settings,
            labelSelector,
            fieldSelector,
            client,
            leadershipScope)
    {
        public Task InvokeOnEventAsync(
            WatchEventType eventType,
            V1OperatorIntegrationTestEntity entity,
            CancellationToken cancellationToken)
            => OnEventAsync(eventType, entity, cancellationToken);
    }
}
