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

public sealed class TimedEntityQueueTest
{
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

        var items = new List<QueueEntry<V1Secret>>();

        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(TimeSpan.FromMilliseconds(500));

        var enumerator = queue.GetAsyncEnumerator(tokenSource.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                items.Add(enumerator.Current);
            }
        }
        catch (OperationCanceledException)
        {
            // We expect to timeout watching the queue so that we can assert the items received
        }

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

        var items = await DrainQueue(queue, TimeSpan.FromMilliseconds(500));

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
            TimeSpan.FromSeconds(5),
            retryCount: 2,
            TestContext.Current.CancellationToken);

        var items = await DrainQueue(queue, TimeSpan.FromMilliseconds(500));

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

        var items = await DrainQueue(queue, TimeSpan.FromMilliseconds(500));

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
            TimeSpan.FromSeconds(5),
            retryCount: 0,
            TestContext.Current.CancellationToken);

        await queue.Enqueue(
            secret,
            ReconciliationType.Deleted,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);

        var items = await DrainQueue(queue, TimeSpan.FromMilliseconds(500));

        items.Should().ContainSingle();
        items[0].ReconciliationType.Should().Be(ReconciliationType.Deleted);
        items[0].ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.ApiServer);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public void Dispose_Should_Complete_Without_Exception()
    {
        using var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

        var act = () => queue.Dispose();

        act.Should().NotThrow();
    }

    private static V1Secret CreateSecret(string secretNamespace, string secretName)
    {
        var secret = new V1Secret();
        secret.EnsureMetadata();

        secret.Metadata.SetNamespace(secretNamespace);
        secret.Metadata.Name = secretName;

        return secret;
    }

    private static async Task<List<QueueEntry<V1Secret>>> DrainQueue(
        TimedEntityQueue<V1Secret> queue,
        TimeSpan timeout)
    {
        var items = new List<QueueEntry<V1Secret>>();
        using var tokenSource = new CancellationTokenSource(timeout);
        var enumerator = queue.GetAsyncEnumerator(tokenSource.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                items.Add(enumerator.Current);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: stop draining after assertion window.
        }

        return items;
    }
}
