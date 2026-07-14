// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Reflection;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.LeaderElection;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Logging;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Test.Watcher;

[Trait("Area", "ScopedLeaderElection")]
public sealed class ScopeAwareSharedResourceWatcherTest
{
    private readonly Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> _queue = new();

    public ScopeAwareSharedResourceWatcherTest()
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
    public async Task Should_Reset_Membership_On_Full_Relist()
    {
        var dispatcher = CreateDispatcher();
        var watcher = CreateWatcher(dispatcher);
        var entity = CreateEntity();

        // Establish membership: the entity enters the pipeline's (match-all) selector.
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);
        VerifyEnqueued(entity, ReconciliationType.Added, Times.Once());

        // A full relist (session from a null resource version) must clear membership so the replayed
        // object is treated as a fresh Added, exactly like the non-scoped shared watcher.
        InvokeOnWatchSessionStarting(watcher, isFullRelist: true);

        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);
        VerifyEnqueued(entity, ReconciliationType.Added, Times.Exactly(2));
    }

    [Fact]
    public async Task Should_Preserve_Membership_On_Resume_Reconnect()
    {
        var dispatcher = CreateDispatcher();
        var watcher = CreateWatcher(dispatcher);
        var entity = CreateEntity();

        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);
        VerifyEnqueued(entity, ReconciliationType.Added, Times.Once());

        // A resume reconnect keeps membership: the still-matching member is a steady-state event, not a
        // second Added.
        InvokeOnWatchSessionStarting(watcher, isFullRelist: false);

        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);
        VerifyEnqueued(entity, ReconciliationType.Added, Times.Once());
        VerifyEnqueued(entity, ReconciliationType.Modified, Times.Once());
    }

    private static void InvokeOnWatchSessionStarting(
        ScopeAwareSharedResourceWatcher<V1OperatorIntegrationTestEntity> watcher, bool isFullRelist)
    {
        // OnWatchSessionStarting is a protected override of a fixed base (ResourceWatcher) signature; the
        // watch loop calls it at each (re)connect. Invoked here to drive the relist/resume branch without
        // spinning up a real watch stream.
        var method = typeof(ScopeAwareSharedResourceWatcher<V1OperatorIntegrationTestEntity>)
            .GetMethod("OnWatchSessionStarting", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(watcher, [isFullRelist]);
    }

    private SharedPipelineDispatcher<V1OperatorIntegrationTestEntity> CreateDispatcher() =>
        new(
            [new SharedPipelineDispatcher<V1OperatorIntegrationTestEntity>.PipelineTarget(
                "pipeline", _queue.Object, new MatchAllSelector())],
            NullLogger.Instance);

    private ScopeAwareSharedResourceWatcher<V1OperatorIntegrationTestEntity> CreateWatcher(
        SharedPipelineDispatcher<V1OperatorIntegrationTestEntity> dispatcher)
    {
        var cacheProvider = Mock.Of<IFusionCacheProvider>();
        Mock.Get(cacheProvider)
            .Setup(cp => cp.GetCache(It.IsAny<string>()))
            .Returns(Mock.Of<IFusionCache>());

        return new ScopeAwareSharedResourceWatcher<V1OperatorIntegrationTestEntity>(
            new ActivitySource("test"),
            Mock.Of<ILogger<ScopeAwareSharedResourceWatcher<V1OperatorIntegrationTestEntity>>>(),
            cacheProvider,
            _queue.Object,
            new OperatorSettingsBuilder().Build(),
            Mock.Of<IEntityLabelSelector<V1OperatorIntegrationTestEntity>>(),
            Mock.Of<IEntityFieldSelector<V1OperatorIntegrationTestEntity>>(),
            Mock.Of<IKubernetesClient>(),
            Mock.Of<ILeadershipScope>(),
            dispatcher,
            Mock.Of<IEntityLoggingScopeFactory<V1OperatorIntegrationTestEntity>>());
    }

    private static V1OperatorIntegrationTestEntity CreateEntity() =>
        new()
        {
            Metadata = new V1ObjectMeta { Name = "test-entity", Uid = "uid-1" },
        };

    private void VerifyEnqueued(V1OperatorIntegrationTestEntity entity, ReconciliationType type, Times times) =>
        _queue.Verify(
            q => q.Enqueue(
                entity,
                type,
                ReconciliationTriggerSource.ApiServer,
                TimeSpan.Zero,
                0,
                It.IsAny<CancellationToken>()),
            times);

    private sealed class MatchAllSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class FakeDedup : ISharedWatchDedup<V1OperatorIntegrationTestEntity>
    {
        public Task<bool> IsDuplicateAsync(WatchEventType eventType, V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task RecordDedupTokenAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveDedupTokenAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
