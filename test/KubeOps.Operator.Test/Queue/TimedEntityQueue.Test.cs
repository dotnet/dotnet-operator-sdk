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
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        await queue.Enqueue(
            CreateSecret("app-ns2", "secret-name"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        var items = new List<V1Secret>();

        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(TimeSpan.FromSeconds(2));

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

    private static V1Secret CreateSecret(string secretNamespace, string secretName)
    {
        var secret = new V1Secret();
        secret.EnsureMetadata();

        secret.Metadata.SetNamespace(secretNamespace);
        secret.Metadata.Name = secretName;

        return secret;
    }
}
