// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Constants;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.Logging;

using Moq;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Test.Watcher;

public sealed class ResourceWatcherTest
{
    [Fact]
    public async Task Restarting_Watcher_Should_Trigger_New_Watch()
    {
        // Arrange.
        var activitySource = new ActivitySource("unit-test");
        var logger = Mock.Of<ILogger<ResourceWatcher<V1Pod>>>();
        var serviceProvider = Mock.Of<IServiceProvider>();
        var timedEntityQueue = new TimedEntityQueue<V1Pod>();
        var operatorSettings = new OperatorSettings { Namespace = "unit-test" };
        var kubernetesClient = Mock.Of<IKubernetesClient>();
        var cache = Mock.Of<IFusionCache>();
        var cacheProvider = Mock.Of<IFusionCacheProvider>();
        var labelSelector = new DefaultEntityLabelSelector<V1Pod>();

        Mock.Get(kubernetesClient)
            .Setup(client => client.WatchAsync<V1Pod>("unit-test", null, null, true, It.IsAny<CancellationToken>()))
            .Returns<string?, string?, string?, bool?, CancellationToken>((_, _, _, _, cancellationToken) => WaitForCancellationAsync<(WatchEventType, V1Pod)>(cancellationToken));

        Mock.Get(cacheProvider)
            .Setup(cp => cp.GetCache(It.Is<string>(s => s == CacheConstants.CacheNames.ResourceWatcher)))
            .Returns(() => cache);

        var resourceWatcher = new ResourceWatcher<V1Pod>(
            activitySource,
            logger,
            serviceProvider,
            timedEntityQueue,
            operatorSettings,
            labelSelector,
            cacheProvider,
            kubernetesClient);

        // Act.
        // Start and stop the watcher.
        await resourceWatcher.StartAsync(CancellationToken.None);
        await resourceWatcher.StopAsync(CancellationToken.None);

        // Restart the watcher.
        await resourceWatcher.StartAsync(CancellationToken.None);

        // Assert.
        Mock.Get(kubernetesClient)
            .Verify(client => client.WatchAsync<V1Pod>("unit-test", null, null, true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static async IAsyncEnumerable<T> WaitForCancellationAsync<T>([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield return default!;
    }
}
