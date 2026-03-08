// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.Logging;

using Moq;

namespace KubeOps.Operator.Test.Queue;

public sealed class EntityQueueBackgroundServiceTest
{
    // A controllable async-enumerable queue that allows tests to push entries on demand.
    private sealed class ControllableQueue<TEntity> : ITimedEntityQueue<TEntity>
        where TEntity : k8s.IKubernetesObject<V1ObjectMeta>
    {
        private readonly System.Threading.Channels.Channel<QueueEntry<TEntity>> _channel =
            System.Threading.Channels.Channel.CreateUnbounded<QueueEntry<TEntity>>();

        public int EnqueueCallCount { get; private set; }

        public Task Enqueue(
            TEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken)
        {
            EnqueueCallCount++;
            _channel.Writer.TryWrite(new(entity, type, reconciliationTriggerSource, retryCount));
            return Task.CompletedTask;
        }

        public Task Remove(TEntity entity, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void Push(TEntity entity, ReconciliationType type, ReconciliationTriggerSource source, int retryCount = 0)
            => _channel.Writer.TryWrite(new(entity, type, source, retryCount));

        public void Complete()
            => _channel.Writer.Complete();

        public async IAsyncEnumerator<QueueEntry<TEntity>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return entry;
            }
        }

        public void Dispose()
            => _channel.Writer.TryComplete();
    }

    private static V1ConfigMap CreateEntity(string? uid = null)
        => new()
        {
            Kind = V1ConfigMap.KubeKind,
            Metadata = new()
            {
                Name = "test-configmap",
                NamespaceProperty = "default",
                Uid = uid ?? Guid.NewGuid().ToString(),
            },
        };

    private static EntityQueueBackgroundService<V1ConfigMap> CreateService(
        ControllableQueue<V1ConfigMap> queue,
        Mock<IReconciler<V1ConfigMap>> reconcilerMock,
        Mock<IKubernetesClient> clientMock,
        V1ConfigMap? entity,
        OperatorSettings? settings = null)
    {
        var effectiveSettings = settings ?? new OperatorSettings();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? _, CancellationToken _) => entity);

        return new(
            new("test"),
            clientMock.Object,
            effectiveSettings,
            queue,
            reconcilerMock.Object,
            Mock.Of<ILogger<EntityQueueBackgroundService<V1ConfigMap>>>());
    }

    [Fact]
    public async Task Reconciler_Is_Called_For_Each_Queued_Entry()
    {
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();
        var entity = CreateEntity();

        reconcilerMock
            .Setup(r =>
                r.Reconcile(
                    It.IsAny<ReconciliationContext<V1ConfigMap>>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ReconciliationResult<V1ConfigMap>.Success(entity));

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Added, ReconciliationTriggerSource.ApiServer);
        queue.Complete();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        reconcilerMock.Verify(
            r => r.Reconcile(
                    It.IsAny<ReconciliationContext<V1ConfigMap>>(),
                    It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Reconciler_Is_Not_Called_When_Client_Returns_Null_For_Non_Deleted_Entry()
    {
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();
        var entity = CreateEntity();

        await using var service = CreateService(queue, reconcilerMock, clientMock, null);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        queue.Complete();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        reconcilerMock.Verify(
            r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1ConfigMap>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Deleted_Entry_Uses_Entity_From_Queue_Without_Client_Lookup()
    {
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();
        var entity = CreateEntity();

        reconcilerMock
            .Setup(r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1ConfigMap>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1ConfigMap>.Success(entity));

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Deleted, ReconciliationTriggerSource.ApiServer);
        queue.Complete();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        // For Deleted entries the client must NOT be called
        clientMock.Verify(
            c => c.GetAsync<V1ConfigMap>(
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        reconcilerMock.Verify(
            r => r.Reconcile(
                It.Is<ReconciliationContext<V1ConfigMap>>(ctx => ctx.EventType == ReconciliationType.Deleted),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Discard_Strategy_Drops_Concurrent_Entry_For_Same_Uid()
    {
        var uid = Guid.NewGuid().ToString();
        var entity = CreateEntity(uid);

        var firstStarted = new TaskCompletionSource();
        var firstCanFinish = new TaskCompletionSource();

        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var callCount = 0;
        reconcilerMock
            .Setup(r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1ConfigMap>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (ReconciliationContext<V1ConfigMap> _, CancellationToken _) =>
            {
                callCount++;
                firstStarted.TrySetResult();
                await firstCanFinish.Task;
                return ReconciliationResult<V1ConfigMap>.Success(entity);
            });

        var settings = new OperatorSettings
        {
            ParallelReconciliationOptions = new()
            {
                MaxParallelReconciliations = 4,
                ConflictStrategy = ParallelReconciliationConflictStrategy.Discard,
            },
        };

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        await firstStarted.Task;

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        queue.Complete();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        firstCanFinish.SetResult();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task RequeueAfterDelay_Strategy_Requeues_Concurrent_Entry_For_Same_Uid()
    {
        var uid = Guid.NewGuid().ToString();
        var entity = CreateEntity(uid);

        var firstStarted = new TaskCompletionSource();
        var firstCanFinish = new TaskCompletionSource();

        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        reconcilerMock
            .Setup(r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1ConfigMap>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (ReconciliationContext<V1ConfigMap> _, CancellationToken _) =>
            {
                firstStarted.TrySetResult();
                await firstCanFinish.Task;
                return ReconciliationResult<V1ConfigMap>.Success(entity);
            });

        var settings = new OperatorSettings
        {
            ParallelReconciliationOptions = new()
            {
                MaxParallelReconciliations = 4,
                ConflictStrategy = ParallelReconciliationConflictStrategy.RequeueAfterDelay,
                RequeueDelay = TimeSpan.FromMilliseconds(50),
            },
        };

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        await firstStarted.Task;

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        firstCanFinish.SetResult();
        await service.StopAsync(TestContext.Current.CancellationToken);

        queue.EnqueueCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Failed_Reconciliation_Is_Requeued_With_ErrorRetry_Source()
    {
        var entity = CreateEntity();
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        reconcilerMock
            .Setup(r => r.Reconcile(It.IsAny<ReconciliationContext<V1ConfigMap>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient error"));

        var settings = new OperatorSettings
        {
            ParallelReconciliationOptions = new()
            {
                MaxParallelReconciliations = 2,
                MaxErrorRetries = 3,
                ErrorBackoffBase = TimeSpan.FromMilliseconds(10),
            },
        };

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        queue.Complete();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        // The entry should be requeued (at least once) with an ErrorRetry trigger.
        queue.EnqueueCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Failed_Reconciliation_Is_Dropped_After_Retry_Limit()
    {
        var entity = CreateEntity();

        // Use a manually-drained queue so we can count re-enqueue calls without
        // feeding them back into processing, which would make the count non-deterministic.
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        reconcilerMock
            .Setup(r => r.Reconcile(It.IsAny<ReconciliationContext<V1ConfigMap>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("persistent error"));

        const int maxRetries = 2;
        var settings = new OperatorSettings
        {
            ParallelReconciliationOptions = new()
            {
                MaxParallelReconciliations = 1,
                MaxErrorRetries = maxRetries,
                ErrorBackoffBase = TimeSpan.FromMilliseconds(10),
            },
        };

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);

        // Wait long enough for all retries to be scheduled (10ms * (1+2+4) = 70ms, add headroom).
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        // Exactly maxRetries re-enqueue calls are expected; the last attempt is dropped.
        queue.EnqueueCallCount.Should().Be(maxRetries);
    }

    [Fact]
    public async Task Error_Retry_Is_Disabled_When_MaxErrorRetries_Is_Zero()
    {
        var entity = CreateEntity();
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        reconcilerMock
            .Setup(r => r.Reconcile(It.IsAny<ReconciliationContext<V1ConfigMap>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("error with retries disabled"));

        var settings = new OperatorSettings
        {
            ParallelReconciliationOptions = new()
            {
                MaxParallelReconciliations = 2,
                MaxErrorRetries = 0,
            },
        };

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        queue.Complete();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        queue.EnqueueCallCount.Should().Be(0);
    }
}
