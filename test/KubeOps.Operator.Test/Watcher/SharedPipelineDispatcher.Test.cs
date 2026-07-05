// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace KubeOps.Operator.Test.Watcher;

public sealed class SharedPipelineDispatcherTest
{
    private readonly Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> _managedQueue = new();
    private readonly Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> _configQueue = new();

    public SharedPipelineDispatcherTest()
    {
        _managedQueue
            .Setup(q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(), It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(), It.IsAny<TimeSpan>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _configQueue
            .Setup(q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(), It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(), It.IsAny<TimeSpan>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Should_Dispatch_To_All_Pipelines_With_Overlapping_Selectors()
    {
        // The scenario from issue #909: two controllers with overlapping label selectors must both
        // receive the event for an entity carrying both labels.
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")),
            ("config", _configQueue.Object, new StringSelector("is-managed=true,autogen-config=true")));

        var entity = CreateEntity(("is-managed", "true"), ("autogen-config", "true"));

        var enqueued = await dispatcher.DispatchAsync(WatchEventType.Modified, entity, CancellationToken.None);

        enqueued.Should().BeTrue();
        VerifyEnqueued(_managedQueue, entity, Times.Once());
        VerifyEnqueued(_configQueue, entity, Times.Once());
    }

    [Fact]
    public async Task Should_Dispatch_Only_To_Matching_Pipelines()
    {
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")),
            ("config", _configQueue.Object, new StringSelector("is-managed=true,autogen-config=true")));

        var entity = CreateEntity(("is-managed", "true"));

        var enqueued = await dispatcher.DispatchAsync(WatchEventType.Modified, entity, CancellationToken.None);

        enqueued.Should().BeTrue();
        VerifyEnqueued(_managedQueue, entity, Times.Once());
        VerifyEnqueued(_configQueue, entity, Times.Never());
    }

    [Fact]
    public async Task Should_Return_False_When_No_Pipeline_Matches()
    {
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("other", "label"));

        var enqueued = await dispatcher.DispatchAsync(WatchEventType.Modified, entity, CancellationToken.None);

        enqueued.Should().BeFalse();
        VerifyEnqueued(_managedQueue, entity, Times.Never());
    }

    [Fact]
    public async Task Should_Use_ClientSide_Selector_Implementation_When_Available()
    {
        var dispatcher = CreateDispatcher(
            ("client-side", _managedQueue.Object, new ClientSideSelector(matches: true)));

        // The client-side implementation matches although the selector string never would.
        var entity = CreateEntity(("other", "label"));

        var enqueued = await dispatcher.DispatchAsync(WatchEventType.Modified, entity, CancellationToken.None);

        enqueued.Should().BeTrue();
        VerifyEnqueued(_managedQueue, entity, Times.Once());
    }

    [Fact]
    public async Task Should_Skip_Pipeline_With_Unparsable_Selector_And_Continue()
    {
        var dispatcher = CreateDispatcher(
            ("broken", _configQueue.Object, new StringSelector("app in nginx")),
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("is-managed", "true"));

        var enqueued = await dispatcher.DispatchAsync(WatchEventType.Modified, entity, CancellationToken.None);

        enqueued.Should().BeTrue();
        VerifyEnqueued(_managedQueue, entity, Times.Once());
        VerifyEnqueued(_configQueue, entity, Times.Never());
    }

    private static SharedPipelineDispatcher<V1OperatorIntegrationTestEntity> CreateDispatcher(
        params (string Key, ITimedEntityQueue<V1OperatorIntegrationTestEntity> Queue, IEntityLabelSelector<V1OperatorIntegrationTestEntity> Selector)[] targets) =>
        new(
            targets
                .Select(t => new SharedPipelineDispatcher<V1OperatorIntegrationTestEntity>.PipelineTarget(t.Key, t.Queue, t.Selector))
                .ToList(),
            NullLogger.Instance);

    private static V1OperatorIntegrationTestEntity CreateEntity(params (string Key, string Value)[] labels) =>
        new()
        {
            Metadata = new()
            {
                Name = "test-entity",
                Uid = Guid.NewGuid().ToString(),
                Labels = labels.ToDictionary(l => l.Key, l => l.Value),
            },
        };

    private static void VerifyEnqueued(
        Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> queue,
        V1OperatorIntegrationTestEntity entity,
        Times times) =>
        queue.Verify(
            q => q.Enqueue(
                entity,
                ReconciliationType.Modified,
                ReconciliationTriggerSource.ApiServer,
                TimeSpan.Zero,
                0,
                It.IsAny<CancellationToken>()),
            times);

    private sealed class StringSelector(string selector) : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(selector);
    }

    private sealed class ClientSideSelector(bool matches)
        : IEntityLabelSelector<V1OperatorIntegrationTestEntity>, IClientSideEntitySelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("never=matches");

        public ValueTask<bool> MatchesAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            ValueTask.FromResult(matches);
    }
}
