// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

namespace KubeOps.Operator.Test.Queue;

public sealed class EntityQueueFactoryTest
{
    [Fact]
    public void Create_Should_Return_Delegate_That_Calls_Enqueue_On_Queue()
    {
        var mockQueue = new Mock<ITimedEntityQueue<V1ConfigMap>>();
        var services = new ServiceCollection()
            .AddSingleton(mockQueue.Object)
            .AddSingleton(Mock.Of<ILogger<EntityQueue<V1ConfigMap>>>())
            .BuildServiceProvider();

        var factory = new EntityQueueFactory(services);
        var enqueue = factory.Create<V1ConfigMap>();

        var entity = new V1ConfigMap { Metadata = new() { Name = "test", Uid = Guid.NewGuid().ToString() } };
        var queueIn = TimeSpan.FromSeconds(10);

        enqueue(
            entity,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            queueIn,
            TestContext.Current.CancellationToken);

        mockQueue
            .Verify(
                q =>
                    q.Enqueue(
                        entity,
                        ReconciliationType.Modified,
                        ReconciliationTriggerSource.Operator,
                        queueIn,
                        TestContext.Current.CancellationToken),
                Times.Once);
    }
}
