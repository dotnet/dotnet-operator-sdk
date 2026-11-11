// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.Logging;

using Moq;

namespace KubeOps.Operator.Test.Queue;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Queue)]
public sealed class TimedEntityQueueTest
{
    // Upper bound for an expected entry to arrive; only reached when something is broken or the runner is busy.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    // How long to keep listening for unexpected entries after the expected ones arrived.
    private static readonly TimeSpan SettlePeriod = TimeSpan.FromMilliseconds(200);

    // A schedule that must never be reached within a drain; used where the test asserts that the earlier
    // schedule of a merged entry wins.
    private static readonly TimeSpan LaterThanDrainTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Can_Enqueue_Multiple_Entities_With_Same_Name()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

        await queue.Enqueue(
            CreateSecret("app-ns1", "secret-name"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromMilliseconds(1),
            retryCount: 0,
            TestContext.Current.CancellationToken);
        await queue.Enqueue(
            CreateSecret("app-ns2", "secret-name"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromMilliseconds(100),
            retryCount: 0,
            TestContext.Current.CancellationToken);

        var items = await DrainQueue(queue, expectedCount: 2);

        items.Select(e => e.Entity).Should().HaveCount(2);
    }

    [Fact]
    public async Task Enqueue_Should_Replace_Later_Schedule_For_Same_Entity()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var secret = CreateSecret("ns", "secret");

        await queue.Enqueue(
            secret,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromMilliseconds(300),
            retryCount: 0,
            TestContext.Current.CancellationToken);

        await queue.Enqueue(
            secret,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);

        var items = await DrainQueue(queue, expectedCount: 1);

        items.Should().ContainSingle();
        items[0].ReconciliationType.Should().Be(ReconciliationType.Modified);
        items[0].ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.ApiServer);
        items[0].RetryCount.Should().Be(0);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task Enqueue_Should_Keep_Earliest_Schedule_For_Same_Entity()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var secret = CreateSecret("ns", "secret");

        await queue.Enqueue(
            secret,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromMilliseconds(50),
            retryCount: 1,
            TestContext.Current.CancellationToken);

        await queue.Enqueue(
            secret,
            ReconciliationType.Added,
            ReconciliationTriggerSource.ApiServer,
            LaterThanDrainTimeout,
            retryCount: 2,
            TestContext.Current.CancellationToken);

        var items = await DrainQueue(queue, expectedCount: 1);

        items.Should().ContainSingle();
        items[0].ReconciliationType.Should().Be(ReconciliationType.Added);
        items[0].ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.ApiServer);
        items[0].RetryCount.Should().Be(2);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task Enqueue_Should_Not_Downgrade_Scheduled_Deleted_Reconciliation()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var secret = CreateSecret("ns", "secret");

        await queue.Enqueue(
            secret,
            ReconciliationType.Deleted,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.FromMilliseconds(50),
            retryCount: 0,
            TestContext.Current.CancellationToken);

        await queue.Enqueue(
            secret,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromMilliseconds(100),
            retryCount: 1,
            TestContext.Current.CancellationToken);

        var items = await DrainQueue(queue, expectedCount: 1);

        items.Should().ContainSingle();
        items[0].ReconciliationType.Should().Be(ReconciliationType.Deleted);
        items[0].ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.Operator);
        items[0].RetryCount.Should().Be(1);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task Enqueue_Should_Replace_Scheduled_Modified_With_Deleted_Reconciliation()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var secret = CreateSecret("ns", "secret");

        await queue.Enqueue(
            secret,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            LaterThanDrainTimeout,
            retryCount: 0,
            TestContext.Current.CancellationToken);

        await queue.Enqueue(
            secret,
            ReconciliationType.Deleted,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);

        var items = await DrainQueue(queue, expectedCount: 1);

        items.Should().ContainSingle();
        items[0].ReconciliationType.Should().Be(ReconciliationType.Deleted);
        items[0].ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.ApiServer);
        queue.Count.Should().Be(0);
    }

    [Fact]
    [Trait(TestTrait.Feature, TestFeature.LeaderLoss)]
    public async Task Clear_Should_Remove_Scheduled_And_Ready_Entries()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

        // One entry ready immediately (gets promoted to the ready queue by the timer) and one scheduled.
        await queue.Enqueue(
            CreateSecret("ns", "ready"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);
        await queue.Enqueue(
            CreateSecret("ns", "scheduled"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromSeconds(5),
            retryCount: 0,
            TestContext.Current.CancellationToken);

        // Give the timer time to promote the ready entry into the ready queue.
        (await WaitUntil(() => queue.ReadyCount == 1, DrainTimeout)).Should().BeTrue();

        queue.Clear();

        queue.Count.Should().Be(0);
        queue.ReadyCount.Should().Be(0);
        (await DrainQueue(queue, expectedCount: 0)).Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTrait.Feature, TestFeature.LeaderLoss)]
    public async Task Enqueue_Should_Be_Dropped_While_Intake_Is_Suspended()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

        queue.SuspendIntake();

        var scheduled = await queue.Enqueue(
            CreateSecret("ns", "secret"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);

        // Returning false lets callers avoid counting a requeue that was not scheduled.
        scheduled.Should().BeFalse();
        queue.Count.Should().Be(0);
        (await DrainQueue(queue, expectedCount: 0)).Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTrait.Feature, TestFeature.LeaderLoss)]
    public async Task Enqueue_Should_Be_Accepted_After_Intake_Is_Resumed()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

        queue.SuspendIntake();
        queue.ResumeIntake();

        var scheduled = await queue.Enqueue(
            CreateSecret("ns", "secret"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);

        scheduled.Should().BeTrue();
        (await DrainQueue(queue, expectedCount: 1)).Should().ContainSingle();
    }

    [Fact]
    [Trait(TestTrait.Feature, TestFeature.LeaderLoss)]
    public async Task Enqueue_Should_Be_Dropped_When_Token_Already_Cancelled()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

        var scheduled = await queue.Enqueue(
            CreateSecret("ns", "secret"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.Zero,
            retryCount: 0,
            new CancellationToken(canceled: true));

        scheduled.Should().BeFalse();
        queue.Count.Should().Be(0);
        (await DrainQueue(queue, expectedCount: 0)).Should().BeEmpty();
    }

    // Regression guard for the TOCTOU race: gate check + scheduling must be atomic with SuspendIntake/Clear.
    // Stressed over many iterations so that a missing lock reliably surfaces (a single parallel round would
    // be probabilistic). Without the lock in Enqueue/Clear, an entry survives the clear and this fails.
    [Fact]
    [Trait(TestTrait.Feature, TestFeature.LeaderLoss)]
    public async Task Concurrent_Enqueue_With_Suspend_And_Clear_Leaves_Nothing()
    {
        const int iterations = 200;
        const int enqueuesPerIteration = 32;
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var i = 0; i < iterations; i++)
        {
            using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

            var enqueues = Enumerable.Range(0, enqueuesPerIteration).Select(n => Task.Run(
                () => queue.Enqueue(
                    CreateSecret("ns", $"secret-{n}"),
                    ReconciliationType.Modified,
                    ReconciliationTriggerSource.Operator,
                    TimeSpan.Zero,
                    retryCount: 0,
                    cancellationToken),
                cancellationToken));

            var stop = Task.Run(
                () =>
                {
                    queue.SuspendIntake();
                    queue.Clear();
                },
                cancellationToken);

            await Task.WhenAll(enqueues.Append(stop));

            // Intake stays suspended, so anything enqueued after the clear is dropped and anything before is
            // cleared: the queue must end up completely empty.
            queue.Count.Should().Be(0);
            queue.ReadyCount.Should().Be(0);
        }
    }

    [Fact]
    public void Dispose_Should_Complete_Without_Exception()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

        var act = queue.Dispose;

        act.Should().NotThrow();
    }

    // Regression guard for #1308: waiting for entries must not block the consumer's thread. The consumer and a
    // probe share a scheduler that runs one task at a time, in order. The probe yields repeatedly, so each of
    // its continuations is queued behind the consumer's. A consumer that blocks its thread while the queue is
    // empty keeps the scheduler occupied and the probe never completes; an asynchronous consumer releases the
    // scheduler while it waits, so the probe completes right away.
    [Fact]
    public async Task Waiting_For_Entries_Should_Not_Block_The_Consumer_Thread()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        using var consumerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, maxConcurrencyLevel: 1)
            .ConcurrentScheduler;
        var factory = new TaskFactory(scheduler);

        var consumer = factory.StartNew(
                async () =>
                {
                    var enumerator = queue.GetAsyncEnumerator(consumerCancellation.Token);
                    try
                    {
                        await enumerator.MoveNextAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected: the consumer is cancelled at the end of the test.
                    }
                },
                TestContext.Current.CancellationToken)
            .Unwrap();

        var probe = factory.StartNew(
                async () =>
                {
                    for (var i = 0; i < 10; i++)
                    {
                        await Task.Yield();
                    }
                },
                TestContext.Current.CancellationToken)
            .Unwrap();

        var completed = await Task.WhenAny(
            probe,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        await consumerCancellation.CancelAsync();
        await consumer;

        completed.Should().Be(probe, "the consumer must not block its thread while it waits for entries");
    }

    [Fact]
    public async Task Enqueue_Should_Not_Merge_Added_Of_Recreated_Object_Into_Pending_Deleted()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var deleted = CreateSecret("ns", "secret", "uid-old");
        var recreated = CreateSecret("ns", "secret", "uid-new");

        await queue.Enqueue(
            deleted,
            ReconciliationType.Deleted,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);
        await queue.Enqueue(
            recreated,
            ReconciliationType.Added,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);

        var items = await DrainQueue(queue, expectedCount: 2);

        items.Should().HaveCount(2);
        items.Should().ContainSingle(e => e.ReconciliationType == ReconciliationType.Deleted)
            .Which.Entity.Uid().Should().Be("uid-old");
        items.Should().ContainSingle(e => e.ReconciliationType == ReconciliationType.Added)
            .Which.Entity.Uid().Should().Be("uid-new");
    }

    [Fact]
    public async Task Enqueue_Should_Not_Merge_Deleted_Retry_Of_Previous_Object_Into_Pending_Added()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var recreated = CreateSecret("ns", "secret", "uid-new");
        var deleted = CreateSecret("ns", "secret", "uid-old");

        await queue.Enqueue(
            recreated,
            ReconciliationType.Added,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);
        await queue.Enqueue(
            deleted,
            ReconciliationType.Deleted,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.FromMilliseconds(100),
            retryCount: 1,
            TestContext.Current.CancellationToken);

        var items = await DrainQueue(queue, expectedCount: 2);

        items.Should().HaveCount(2);
        items.Should().ContainSingle(e => e.ReconciliationType == ReconciliationType.Added)
            .Which.Entity.Uid().Should().Be("uid-new");
        items.Should().ContainSingle(e => e.ReconciliationType == ReconciliationType.Deleted)
            .Which.Entity.Uid().Should().Be("uid-old");
    }

    [Fact]
    public async Task Enqueue_Should_Merge_Entries_Of_Same_Uid()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

        await queue.Enqueue(
            CreateSecret("ns", "secret", "uid"),
            ReconciliationType.Deleted,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.FromMilliseconds(100),
            retryCount: 0,
            TestContext.Current.CancellationToken);
        await queue.Enqueue(
            CreateSecret("ns", "secret", "uid"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromMilliseconds(100),
            retryCount: 0,
            TestContext.Current.CancellationToken);

        queue.Count.Should().Be(1);

        var items = await DrainQueue(queue, expectedCount: 1);

        items.Should().ContainSingle()
            .Which.ReconciliationType.Should().Be(ReconciliationType.Deleted);
    }

    [Fact]
    public void GetKey_Should_Use_Uid_And_Fall_Back_To_Namespace_And_Name()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

        queue.GetKey(CreateSecret("ns", "secret", "uid")).Should().Be("uid");
        queue.GetKey(CreateSecret("ns", "secret")).Should().Be("ns/secret");
    }

    [Fact]
    public async Task Timer_Should_Keep_The_Entry_That_Replaced_A_Cancelled_One()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var secret = CreateSecret("ns", "secret");
        var key = queue.GetKey(secret)!;
        await queue.Enqueue(
            secret,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.ApiServer,
            LaterThanDrainTimeout,
            retryCount: 0,
            TestContext.Current.CancellationToken);
        var replaced = queue.GetScheduledEntry(key)!;
        await queue.Enqueue(
            secret,
            ReconciliationType.Deleted,
            ReconciliationTriggerSource.ApiServer,
            LaterThanDrainTimeout,
            retryCount: 0,
            TestContext.Current.CancellationToken);
        var replacement = queue.GetScheduledEntry(key);

        // The timer loop read the entry before Enqueue replaced and cancelled it.
        queue.ProcessScheduledEntry(key, replaced, DateTimeOffset.UtcNow);

        queue.GetScheduledEntry(key).Should().BeSameAs(replacement);
        queue.ReadyCount.Should().Be(0);
    }

    [Fact]
    public async Task Timer_Should_Not_Promote_An_Entry_That_Was_Replaced()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var secret = CreateSecret("ns", "secret");
        var key = queue.GetKey(secret)!;
        await queue.Enqueue(
            secret,
            ReconciliationType.Deleted,
            ReconciliationTriggerSource.ApiServer,
            LaterThanDrainTimeout,
            retryCount: 0,
            TestContext.Current.CancellationToken);
        var replacement = queue.GetScheduledEntry(key);

        // A due entry the timer loop read and found not cancelled, before Enqueue replaced it.
        var stale = new TimedQueueEntry<V1Secret>(
            secret,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.Zero,
            retryCount: 0);
        queue.ProcessScheduledEntry(key, stale, DateTimeOffset.UtcNow);

        queue.GetScheduledEntry(key).Should().BeSameAs(replacement);
        queue.ReadyCount.Should().Be(0);
    }

    [Fact]
    public async Task Timer_Should_Promote_The_Scheduled_Entry_Once_It_Is_Due()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var secret = CreateSecret("ns", "secret");
        var key = queue.GetKey(secret)!;
        await queue.Enqueue(
            secret,
            ReconciliationType.Deleted,
            ReconciliationTriggerSource.ApiServer,
            LaterThanDrainTimeout,
            retryCount: 0,
            TestContext.Current.CancellationToken);
        var scheduled = queue.GetScheduledEntry(key)!;

        queue.ProcessScheduledEntry(key, scheduled, scheduled.EnqueueAt);

        queue.GetScheduledEntry(key).Should().BeNull();
        var items = await DrainQueue(queue, expectedCount: 1);
        items.Should().ContainSingle().Which.ReconciliationType.Should().Be(ReconciliationType.Deleted);
    }

    private static V1Secret CreateSecret(string secretNamespace, string secretName)
    {
        var secret = new V1Secret();
        secret.EnsureMetadata();

        secret.Metadata.SetNamespace(secretNamespace);
        secret.Metadata.Name = secretName;

        return secret;
    }

    private static V1Secret CreateSecret(string secretNamespace, string secretName, string uid)
    {
        var secret = CreateSecret(secretNamespace, secretName);
        secret.Metadata.Uid = uid;
        return secret;
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        return condition();
    }

    // Collects entries until the expected number has arrived (or DrainTimeout elapsed), then keeps listening
    // for SettlePeriod so that unexpected additional entries still surface. Waiting for the expected count
    // instead of a fixed window keeps the tests stable on a busy CI runner: both the queue's timer loop and
    // the consumer need a thread pool thread, and when parallel tests saturate the pool, getting one can
    // take seconds (#1305). With expectedCount 0 only the settle period is awaited.
    private static async Task<List<QueueEntry<V1Secret>>> DrainQueue(
        TimedEntityQueue<V1Secret> queue,
        int expectedCount)
    {
        var items = new List<QueueEntry<V1Secret>>();
        using var tokenSource = new CancellationTokenSource(expectedCount == 0 ? SettlePeriod : DrainTimeout);
        var enumerator = queue.GetAsyncEnumerator(tokenSource.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                items.Add(enumerator.Current);
                if (items.Count == expectedCount)
                {
                    tokenSource.CancelAfter(SettlePeriod);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: stop draining after the timeout or the settle period.
        }

        return items;
    }
}
