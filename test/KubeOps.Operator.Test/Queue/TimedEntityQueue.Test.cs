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
        var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

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

        var items = new List<V1Secret>();

        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(TimeSpan.FromMilliseconds(500));

        var enumerator = queue.GetAsyncEnumerator(tokenSource.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                items.Add(enumerator.Current.Entity);
            }
        }
        catch (OperationCanceledException)
        {
            // We expect to timeout watching the queue so that we can assert the items received
        }

        items.Count.Should().Be(2);
    }

    [Fact]
    public async Task Cancelled_Entry_Should_Not_Be_Promoted_To_Queue()
    {
        var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var secret = CreateSecret("ns", "secret");

        await queue.Enqueue(
            secret,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromMilliseconds(200),
            retryCount: 0,
            TestContext.Current.CancellationToken);

        // Cancel before the entry is promoted
        await queue.Remove(secret, TestContext.Current.CancellationToken);

        var items = new List<V1Secret>();
        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(TimeSpan.FromMilliseconds(500));

        var enumerator = queue.GetAsyncEnumerator(tokenSource.Token);
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                items.Add(enumerator.Current.Entity);
            }
        }
        catch (OperationCanceledException e) when (e.CancellationToken != TestContext.Current.CancellationToken)
        {
            // We expect to timeout watching the queue so that we can assert the items received
        }

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_Should_Prevent_Entry_From_Being_Promoted()
    {
        var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());
        var secret = CreateSecret("ns", "removable-secret");

        await queue.Enqueue(
            secret,
            ReconciliationType.Added,
            ReconciliationTriggerSource.ApiServer,
            TimeSpan.FromMilliseconds(300),
            retryCount: 0,
            TestContext.Current.CancellationToken);

        await queue.Remove(secret, TestContext.Current.CancellationToken);

        queue.Count.Should().Be(0);
    }

    [Fact]
    public void Dispose_Should_Complete_Without_Exception()
    {
        var queue = new TimedEntityQueue<V1Secret>(Mock.Of<ILogger<TimedEntityQueue<V1Secret>>>());

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
}
