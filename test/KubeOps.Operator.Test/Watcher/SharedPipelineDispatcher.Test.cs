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

[Trait("Area", "MultipleControllers")]
public sealed class SharedPipelineDispatcherTest
{
    private readonly Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> _managedQueue = new();
    private readonly Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> _configQueue = new();

    public SharedPipelineDispatcherTest()
    {
        SetupEnqueue(_managedQueue, scheduled: true);
        SetupEnqueue(_configQueue, scheduled: true);
    }

    [Fact]
    public async Task Should_Dispatch_Entry_To_All_Pipelines_With_Overlapping_Selectors()
    {
        // The scenario from issue #909: two controllers with overlapping label selectors must both receive
        // the event for an entity carrying both labels. On first sight the object enters both selectors, so
        // both get an Added (mirroring a server-side filtered watch).
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")),
            ("config", _configQueue.Object, new StringSelector("is-managed=true,autogen-config=true")));

        var entity = CreateEntity(("is-managed", "true"), ("autogen-config", "true"));

        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        VerifyEnqueued(_managedQueue, entity, ReconciliationType.Added, Times.Once());
        VerifyEnqueued(_configQueue, entity, ReconciliationType.Added, Times.Once());
    }

    [Fact]
    public async Task Should_Dispatch_Only_To_Matching_Pipelines()
    {
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")),
            ("config", _configQueue.Object, new StringSelector("is-managed=true,autogen-config=true")));

        var entity = CreateEntity(("is-managed", "true"));

        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        VerifyEnqueued(_managedQueue, entity, ReconciliationType.Added, Times.Once());
        VerifyAnyEnqueued(_configQueue, Times.Never());
    }

    [Fact]
    public async Task Should_Not_Dispatch_When_No_Pipeline_Matches()
    {
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("other", "label"));
        var dedup = new FakeDedup();

        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, dedup, CancellationToken.None);

        VerifyAnyEnqueued(_managedQueue, Times.Never());
        dedup.RecordCount.Should().Be(0);
    }

    [Fact]
    public async Task Should_Dispatch_Steady_Event_To_Existing_Member_Behind_A_Single_Dedup()
    {
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("is-managed", "true"));

        // First event: entry (Added), becomes a member.
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        // Second event on the same object while still matching is steady state: it goes through dedup.
        var notDuplicate = new FakeDedup(isDuplicate: false);
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, notDuplicate, CancellationToken.None);

        VerifyEnqueued(_managedQueue, entity, ReconciliationType.Modified, Times.Once());
        notDuplicate.RecordCount.Should().Be(1);
    }

    [Fact]
    public async Task Should_Skip_Steady_Event_When_Deduplicated()
    {
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("is-managed", "true"));
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        var duplicate = new FakeDedup(isDuplicate: true);
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, duplicate, CancellationToken.None);

        // Only the initial entry was dispatched; the deduplicated steady event was not.
        VerifyEnqueued(_managedQueue, entity, ReconciliationType.Modified, Times.Never());
        duplicate.RecordCount.Should().Be(0);
    }

    [Fact]
    public async Task Should_Dispatch_Synthetic_Delete_When_Object_Leaves_A_Selector()
    {
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("is-managed", "true"));
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        // The label is removed so the object no longer matches — the pipeline must get a Deleted, exactly as
        // a server-side filtered watch would deliver on selector exit.
        entity.Metadata.Labels!.Remove("is-managed");
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        VerifyEnqueued(_managedQueue, entity, ReconciliationType.Deleted, Times.Once());
    }

    [Fact]
    public async Task Should_Not_Double_Dispatch_On_Entry()
    {
        // An entry is handled by the transition pass only; the steady pass must not also serve it.
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("is-managed", "true"));

        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(isDuplicate: false), CancellationToken.None);

        VerifyAnyEnqueued(_managedQueue, Times.Once());
    }

    [Fact]
    public async Task Should_Keep_Membership_Unchanged_When_Enqueue_Is_Dropped()
    {
        // A suspended queue drops the entry (returns false); the object must not become a member, so the next
        // matching event is treated as an entry again (not a deduped steady event).
        SetupEnqueue(_managedQueue, scheduled: false);
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("is-managed", "true"));
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        // Now the queue accepts again; because membership was never set, this is still an entry (Added).
        SetupEnqueue(_managedQueue, scheduled: true);
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        VerifyEnqueued(_managedQueue, entity, ReconciliationType.Added, Times.Exactly(2));
    }

    [Fact]
    public async Task Should_Reset_Membership_So_No_Exit_Is_Synthesized_After_A_Full_Relist()
    {
        var dispatcher = CreateDispatcher(
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("is-managed", "true"));
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        // A full relist clears membership. A subsequent non-matching event must NOT synthesize a Deleted,
        // because the object is no longer a known member.
        dispatcher.ResetMembership();
        entity.Metadata.Labels!.Remove("is-managed");
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        VerifyEnqueued(_managedQueue, entity, ReconciliationType.Deleted, Times.Never());
    }

    [Fact]
    public async Task Should_Use_ClientSide_Selector_Implementation_When_Available()
    {
        var dispatcher = CreateDispatcher(
            ("client-side", _managedQueue.Object, new ClientSideSelector(matches: true)));

        // The client-side implementation matches although the selector string never would.
        var entity = CreateEntity(("other", "label"));

        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        VerifyEnqueued(_managedQueue, entity, ReconciliationType.Added, Times.Once());
    }

    [Fact]
    public async Task Should_Skip_Pipeline_With_Unparsable_Selector_And_Continue()
    {
        var dispatcher = CreateDispatcher(
            ("broken", _configQueue.Object, new StringSelector("app in nginx")),
            ("managed", _managedQueue.Object, new StringSelector("is-managed=true")));

        var entity = CreateEntity(("is-managed", "true"));

        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, new FakeDedup(), CancellationToken.None);

        VerifyEnqueued(_managedQueue, entity, ReconciliationType.Added, Times.Once());
        VerifyAnyEnqueued(_configQueue, Times.Never());
    }

    [Fact]
    public async Task Should_Not_Advance_Dedup_Token_When_A_Steady_Enqueue_Is_Dropped()
    {
        // Two existing members. On a steady event one accepts and the other drops (e.g. its intake was
        // momentarily suspended). The shared token must NOT advance, so the re-delivered event still reaches
        // the dropped member instead of being deduplicated away.
        var dispatcher = CreateDispatcher(
            ("a", _managedQueue.Object, new StringSelector("is-managed=true")),
            ("b", _configQueue.Object, new StringSelector("is-managed=true")));
        var dedup = new RecordingDedup();
        var entity = CreateEntity(("is-managed", "true"));

        // 1. Entry: both become members (bypasses dedup, records the token for version "1").
        entity.Metadata.ResourceVersion = "1";
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, dedup, CancellationToken.None);

        // 2. A steady change (version "2"): A accepts, B drops.
        SetupEnqueue(_configQueue, scheduled: false);
        entity.Metadata.ResourceVersion = "2";
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, dedup, CancellationToken.None);

        // 3. The same change is re-delivered; B accepts now and must receive it (token was not advanced).
        SetupEnqueue(_configQueue, scheduled: true);
        await dispatcher.ProcessEventAsync(WatchEventType.Modified, entity, dedup, CancellationToken.None);

        // B was dispatched the steady event twice (the dropped attempt and the successful re-delivery); with
        // a prematurely advanced token the re-delivery would have been deduplicated and B would get it once.
        VerifyEnqueued(_configQueue, entity, ReconciliationType.Modified, Times.Exactly(2));
    }

    private static void SetupEnqueue(Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> queue, bool scheduled) =>
        queue
            .Setup(q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(), It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(), It.IsAny<TimeSpan>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduled);

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
        ReconciliationType type,
        Times times) =>
        queue.Verify(
            q => q.Enqueue(
                entity,
                type,
                ReconciliationTriggerSource.ApiServer,
                TimeSpan.Zero,
                0,
                It.IsAny<CancellationToken>()),
            times);

    private static void VerifyAnyEnqueued(
        Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>> queue,
        Times times) =>
        queue.Verify(
            q => q.Enqueue(
                It.IsAny<V1OperatorIntegrationTestEntity>(),
                It.IsAny<ReconciliationType>(),
                It.IsAny<ReconciliationTriggerSource>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
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

    private sealed class FakeDedup(bool isDuplicate = false) : ISharedWatchDedup<V1OperatorIntegrationTestEntity>
    {
        public int RecordCount { get; private set; }

        public int RemoveCount { get; private set; }

        public Task<bool> IsDuplicateAsync(WatchEventType eventType, V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(isDuplicate);

        public Task RecordDedupTokenAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            RecordCount++;
            return Task.CompletedTask;
        }

        public Task RemoveDedupTokenAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            RemoveCount++;
            return Task.CompletedTask;
        }
    }

    // A stateful deduplication fake keyed on resourceVersion, mirroring the real watcher: an event is a
    // duplicate once its version's token has been recorded (and not removed).
    private sealed class RecordingDedup : ISharedWatchDedup<V1OperatorIntegrationTestEntity>
    {
        private string? _recordedVersion;

        public Task<bool> IsDuplicateAsync(WatchEventType eventType, V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(_recordedVersion is not null && _recordedVersion == entity.Metadata.ResourceVersion);

        public Task RecordDedupTokenAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            _recordedVersion = entity.Metadata.ResourceVersion;
            return Task.CompletedTask;
        }

        public Task RemoveDedupTokenAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            _recordedVersion = null;
            return Task.CompletedTask;
        }
    }
}
