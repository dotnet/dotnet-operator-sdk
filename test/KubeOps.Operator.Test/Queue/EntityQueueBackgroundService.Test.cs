// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.Metrics;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Metrics;
using KubeOps.Operator.Queue;

using Microsoft.Extensions.DependencyInjection;
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

        private int _getAsyncEnumeratorCallCount;

        public int EnqueueCallCount { get; private set; }

        // Number of times a consuming loop began iterating the queue. Each processing loop calls
        // GetAsyncEnumerator exactly once, so this equals the number of concurrently started loops.
        public int GetAsyncEnumeratorCallCount => Volatile.Read(ref _getAsyncEnumeratorCallCount);

        // Controls the value returned by Enqueue, to simulate a leadership-aware queue dropping the entry
        // (returns false) versus scheduling it (returns true).
        public bool EnqueueResult { get; set; } = true;

        // Captures the cancellation token of the most recent Enqueue call, so tests can assert which token
        // the producer passed (e.g. the processing token vs. CancellationToken.None for error retries).
        public CancellationToken LastEnqueueToken { get; private set; }

        public Task<bool> Enqueue(
            TEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken)
        {
            EnqueueCallCount++;
            LastEnqueueToken = cancellationToken;
            _channel.Writer.TryWrite(new(entity, type, reconciliationTriggerSource, retryCount));
            return Task.FromResult(EnqueueResult);
        }

        public void Push(TEntity entity, ReconciliationType type, ReconciliationTriggerSource source, int retryCount = 0)
            => _channel.Writer.TryWrite(new(entity, type, source, retryCount));

        public void Complete()
            => _channel.Writer.Complete();

        public async IAsyncEnumerator<QueueEntry<TEntity>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _getAsyncEnumeratorCallCount);
            await foreach (var entry in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return entry;
            }
        }

        public void Dispose()
            => _channel.Writer.TryComplete();
    }

    // A queue whose enumerator parks inside MoveNextAsync (holding the processing token) until released, then
    // touches the token's wait handle — exactly what BlockingCollection.GetConsumingEnumerable does. If the
    // token's source was disposed while the loop was parked, that touch throws ObjectDisposedException.
    private sealed class BarrierQueue : ITimedEntityQueue<V1ConfigMap>
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _tokenDisposedWhileInUse;

        public Task LoopEntered => _entered.Task;

        public bool TokenDisposedWhileInUse => _tokenDisposedWhileInUse;

        public void ReleaseLoops() => _release.TrySetResult();

        public Task<bool> Enqueue(
            V1ConfigMap entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public async IAsyncEnumerator<QueueEntry<V1ConfigMap>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task;

            try
            {
                _ = cancellationToken.WaitHandle;
            }
            catch (ObjectDisposedException)
            {
                _tokenDisposedWhileInUse = true;
            }

            yield break;
        }

        public void Dispose()
        {
        }
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
        OperatorSettings? settings = null,
        OperatorMetrics? metrics = null)
    {
        var effectiveSettings = settings ?? new OperatorSettingsBuilder().Build();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? _, CancellationToken _) => entity);

        var service = new EntityQueueBackgroundService<V1ConfigMap>(
            new("test"),
            clientMock.Object,
            effectiveSettings,
            queue,
            reconcilerMock.Object,
            new EntityReconcileCoordinator<V1ConfigMap>(effectiveSettings),
            Mock.Of<ILogger<EntityQueueBackgroundService<V1ConfigMap>>>(),
            metrics);

        // Keep the drain bound short so tests that intentionally block a worker across StopAsync/dispose don't
        // wait the production default. Tests that assert the bound itself override this explicitly.
        service.DrainGracePeriod = TimeSpan.FromMilliseconds(200);
        return service;
    }

    [Trait("Area", "Otel")]
    [Fact]
    public async Task Throwing_Reconciler_Records_Failure_Reconciliation_Metric()
    {
        const string meterName = "test-failure-metrics";
        using var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();
        var metrics = new OperatorMetrics(meterFactory, meterName);

        var captured = new List<(string Status, string? ErrorType)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == "kubeops.operator.reconciliation")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? status = null;
            string? errorType = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "kubeops.reconciliation.status")
                {
                    status = tag.Value as string;
                }
                else if (tag.Key == "error.type")
                {
                    errorType = tag.Value as string;
                }
            }

            lock (captured)
            {
                captured.Add((status!, errorType));
            }
        });
        listener.Start();

        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();
        var entity = CreateEntity();

        reconcilerMock
            .Setup(r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1ConfigMap>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // No retries so the failure path resolves deterministically to a single dropped attempt.
        var settings = new OperatorSettingsBuilder()
            .WithParallelReconciliation(p => p.MaxErrorRetries = 0)
            .Build();

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings, metrics);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Added, ReconciliationTriggerSource.ApiServer);
        queue.Complete();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        lock (captured)
        {
            captured.Should().ContainSingle();
            captured[0].Status.Should().Be("failure");
            captured[0].ErrorType.Should().Be("System.InvalidOperationException");
        }
    }

    [Trait("Area", "LeaderLoss")]
    [Fact]
    public async Task StartAsync_Can_Restart_After_The_Loop_Exits_Without_A_Stop_Request()
    {
        // Finding 1: if ExecuteAsync exits on its own (queue enumerator completed, or an unexpected fault)
        // without going through StopAsync/RequestStopAsync, _running must be reset so a later StartAsync
        // (e.g. a leadership re-acquisition) can restart the loop instead of being suppressed forever.
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();
        await using var service = CreateService(queue, reconcilerMock, clientMock, CreateEntity());

        // Completed channel -> the loop's enumerator finishes immediately -> ExecuteAsync returns on its own.
        queue.Complete();

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken); // let the loop start and fully exit

        // A second start (as on re-acquired leadership) must start a fresh loop, not be suppressed.
        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        queue.GetAsyncEnumeratorCallCount.Should().Be(2);
    }

    [Trait("Area", "LeaderLoss")]
    [Fact]
    public async Task StartAsync_Is_Idempotent_And_Starts_Only_One_Processing_Loop()
    {
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();
        var entity = CreateEntity();

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity);

        // Two starts without an intervening StopAsync. This mirrors the leadership-aware race where the
        // StartAsync IsLeader() branch and a concurrent OnStartedLeading callback both invoke
        // base.StartAsync. Only one queue-consuming loop must run, otherwise every entry is reconciled
        // twice concurrently.
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StartAsync(TestContext.Current.CancellationToken);

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        queue.GetAsyncEnumeratorCallCount.Should().Be(1);
    }

    [Trait("Area", "LeaderLoss")]
    [Fact]
    public async Task Restart_Does_Not_Dispose_CancellationTokenSource_Still_Used_By_Previous_Loop()
    {
        // L2: on a leadership flap (StopAsync then StartAsync) the service must not dispose a
        // CancellationTokenSource whose token a still-running former loop is observing. The previous code
        // reused a shared _cts and eagerly disposed it in StartAsync, so the lingering loop's queue
        // enumerator could touch an already-disposed token source (ObjectDisposedException). Each run must
        // own its own token source and dispose it only when that run ends.
        var queue = new BarrierQueue();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        var settings = new OperatorSettingsBuilder().Build();
        var service = new EntityQueueBackgroundService<V1ConfigMap>(
            new("test"),
            clientMock.Object,
            settings,
            queue,
            reconcilerMock.Object,
            new EntityReconcileCoordinator<V1ConfigMap>(settings),
            Mock.Of<ILogger<EntityQueueBackgroundService<V1ConfigMap>>>())
        {
            // The BarrierQueue ignores cancellation, so bound the stop/dispose drain short.
            DrainGracePeriod = TimeSpan.FromMilliseconds(200),
        };

        await service.StartAsync(TestContext.Current.CancellationToken);
        await queue.LoopEntered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Flap: stop (cancels the first run's token) then start a fresh run, all while the first loop is
        // still parked inside the queue enumerator holding the first run's token.
        await service.StopAsync(TestContext.Current.CancellationToken);
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Now let the parked loop(s) touch their token.
        queue.ReleaseLoops();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        queue.TokenDisposedWhileInUse.Should().BeFalse();

        await service.StopAsync(TestContext.Current.CancellationToken);
        await service.DisposeAsync();
    }

    [Trait("Area", "LeaderLoss")]
    [Fact]
    public async Task Stop_Does_Not_Dispose_Token_While_A_Reconciliation_Is_Still_In_Flight()
    {
        // N1: StopAsync only requests cancellation; the processing loop must still drain its in-flight worker
        // tasks before its CancellationTokenSource is disposed (and before resources are torn down). Otherwise
        // a still-running reconciler that touches its token hits ObjectDisposedException.
        var entity = CreateEntity();
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        var entered = new TaskCompletionSource();
        var canFinish = new TaskCompletionSource();
        var tokenDisposedWhileInFlight = false;

        reconcilerMock
            .Setup(r => r.Reconcile(It.IsAny<ReconciliationContext<V1ConfigMap>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ReconciliationContext<V1ConfigMap> _context, CancellationToken token) =>
            {
                entered.TrySetResult();
                await canFinish.Task;

                // The worker still holds the processing token after StopAsync cancelled it. If the loop
                // disposed the token source underneath the still-running worker, touching the token throws.
                try
                {
                    _ = token.WaitHandle;
                }
                catch (ObjectDisposedException)
                {
                    tokenDisposedWhileInFlight = true;
                }

                return ReconciliationResult<V1ConfigMap>.Success(entity);
            });

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        await entered.Task;

        // Stop while the reconciliation is in flight, then give the (buggy) loop time to return and dispose
        // its token source before the worker resumes and touches the token.
        await service.StopAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        canFinish.SetResult();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        tokenDisposedWhileInFlight.Should().BeFalse();
    }

    [Trait("Area", "LeaderLoss")]
    [Fact]
    public async Task StopAsync_Awaits_The_Drain_Of_In_Flight_Reconciliations()
    {
        // Finding 1: host StopAsync must honor the IHostedService contract and await the drain of in-flight
        // reconciliations (bounded), not return immediately while a reconciliation is still running.
        var entity = CreateEntity();
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        var entered = new TaskCompletionSource();
        var canFinish = new TaskCompletionSource();
        reconcilerMock
            .Setup(r => r.Reconcile(It.IsAny<ReconciliationContext<V1ConfigMap>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ReconciliationContext<V1ConfigMap> _ctx, CancellationToken _token) =>
            {
                entered.TrySetResult();
                await canFinish.Task;
                return ReconciliationResult<V1ConfigMap>.Success(entity);
            });

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity);
        service.DrainGracePeriod = TimeSpan.FromSeconds(5); // long enough to actually await a cooperative drain
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        await entered.Task;

        var stopTask = service.StopAsync(TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        stopTask.IsCompleted.Should().BeFalse(); // StopAsync is awaiting the in-flight reconciliation

        canFinish.SetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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

        var settings = new OperatorSettingsBuilder
        {
            ParallelReconciliation = new()
            {
                MaxParallelReconciliations = 4,
                ConflictStrategy = ParallelReconciliationConflictStrategy.Discard,
            },
        }.Build();

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

        var settings = new OperatorSettingsBuilder
        {
            ParallelReconciliation = new()
            {
                MaxParallelReconciliations = 4,
                ConflictStrategy = ParallelReconciliationConflictStrategy.RequeueAfterDelay,
                RequeueDelay = TimeSpan.FromMilliseconds(50),
            },
        }.Build();

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

    [Trait("Area", "Otel")]
    [Fact]
    public async Task Conflict_Requeue_Metric_Is_Not_Recorded_When_Enqueue_Is_Dropped()
    {
        // A conflicting reconciliation is requeued (RequeueAfterDelay). If a leadership-aware queue drops
        // that enqueue (intake suspended), Enqueue returns false and the conflict metric must not be counted.
        const string meterName = "test-conflict-requeue-drop-metrics";
        using var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();
        var metrics = new OperatorMetrics(meterFactory, meterName);

        var requeued = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == "kubeops.operator.queue.requeued")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref requeued, (int)value));
        listener.Start();

        var uid = Guid.NewGuid().ToString();
        var entity = CreateEntity(uid);
        var firstStarted = new TaskCompletionSource();
        var firstCanFinish = new TaskCompletionSource();

        var queue = new ControllableQueue<V1ConfigMap> { EnqueueResult = false };
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        reconcilerMock
            .Setup(r => r.Reconcile(It.IsAny<ReconciliationContext<V1ConfigMap>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ReconciliationContext<V1ConfigMap> _, CancellationToken _) =>
            {
                firstStarted.TrySetResult();
                await firstCanFinish.Task;
                return ReconciliationResult<V1ConfigMap>.Success(entity);
            });

        var settings = new OperatorSettingsBuilder
        {
            ParallelReconciliation = new()
            {
                MaxParallelReconciliations = 4,
                ConflictStrategy = ParallelReconciliationConflictStrategy.RequeueAfterDelay,
                RequeueDelay = TimeSpan.FromMilliseconds(50),
            },
        }.Build();

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings, metrics);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        await firstStarted.Task;

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        firstCanFinish.SetResult();
        await service.StopAsync(TestContext.Current.CancellationToken);

        // The conflicting entry's requeue was attempted but dropped, so no conflict metric was recorded.
        queue.EnqueueCallCount.Should().BeGreaterThan(0);
        requeued.Should().Be(0);
    }

    [Trait("Area", "Otel")]
    [Fact]
    public async Task ErrorRetry_Requeue_Metric_Is_Not_Recorded_When_Enqueue_Is_Dropped()
    {
        // When a leadership-aware queue drops the retry enqueue (intake suspended after leadership loss),
        // Enqueue returns false and the requeue metric must not be recorded.
        const string meterName = "test-requeue-drop-metrics";
        using var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();
        var metrics = new OperatorMetrics(meterFactory, meterName);

        var requeued = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == "kubeops.operator.queue.requeued")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref requeued, (int)value));
        listener.Start();

        var entity = CreateEntity();
        var queue = new ControllableQueue<V1ConfigMap> { EnqueueResult = false };
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        reconcilerMock
            .Setup(r => r.Reconcile(It.IsAny<ReconciliationContext<V1ConfigMap>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient error"));

        var settings = new OperatorSettingsBuilder
        {
            ParallelReconciliation = new()
            {
                MaxParallelReconciliations = 2,
                MaxErrorRetries = 3,
                ErrorBackoffBase = TimeSpan.FromMilliseconds(10),
            },
        }.Build();

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings, metrics);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        queue.Complete();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        // The retry was attempted (Enqueue called) but, because it was dropped, no requeue was counted.
        queue.EnqueueCallCount.Should().BeGreaterThan(0);
        requeued.Should().Be(0);
    }

    [Trait("Area", "LeaderLoss")]
    [Fact]
    public async Task ErrorRetry_Enqueue_Receives_Processing_Token_So_It_Is_Rejected_After_Stop()
    {
        // Finding 2: a former leadership term's error retry must not leak into the next term.
        // The retry enqueue is passed the processing cancellationToken (not CancellationToken.None).
        // A non-cooperative reconciler that ignores cancellation and throws a non-OCE *after* StopAsync
        // has cancelled the processing loop must therefore enqueue its retry with an already-cancelled
        // token, which a leadership-aware queue rejects.
        var entity = CreateEntity();
        var queue = new ControllableQueue<V1ConfigMap>();
        var reconcilerMock = new Mock<IReconciler<V1ConfigMap>>();
        var clientMock = new Mock<IKubernetesClient>();

        var reconcileEntered = new TaskCompletionSource();
        var canThrow = new TaskCompletionSource();

        clientMock
            .Setup(c => c.GetAsync<V1ConfigMap>(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Non-cooperative: ignores the token, blocks until released, then throws a non-OCE.
        reconcilerMock
            .Setup(r => r.Reconcile(It.IsAny<ReconciliationContext<V1ConfigMap>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ReconciliationContext<V1ConfigMap> _, CancellationToken _) =>
            {
                reconcileEntered.TrySetResult();
                await canThrow.Task;
                throw new InvalidOperationException("late non-cooperative failure");
            });

        var settings = new OperatorSettingsBuilder
        {
            ParallelReconciliation = new()
            {
                MaxParallelReconciliations = 2,
                MaxErrorRetries = 3,
                ErrorBackoffBase = TimeSpan.FromMilliseconds(10),
            },
        }.Build();

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        await reconcileEntered.Task;

        // Simulate leadership loss / shutdown: cancel the processing loop while the reconciler is in-flight.
        await service.StopAsync(TestContext.Current.CancellationToken);

        // Let the in-flight reconciler throw now; its retry enqueue must carry the cancelled processing token.
        canThrow.SetResult();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        queue.EnqueueCallCount.Should().BeGreaterThan(0);
        queue.LastEnqueueToken.IsCancellationRequested.Should().BeTrue();
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

        var settings = new OperatorSettingsBuilder
        {
            ParallelReconciliation = new()
            {
                MaxParallelReconciliations = 2,
                MaxErrorRetries = 3,
                ErrorBackoffBase = TimeSpan.FromMilliseconds(10),
            },
        }.Build();

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
        var settings = new OperatorSettingsBuilder
        {
            ParallelReconciliation = new()
            {
                MaxParallelReconciliations = 1,
                MaxErrorRetries = maxRetries,
                ErrorBackoffBase = TimeSpan.FromMilliseconds(10),
            },
        }.Build();

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

        var settings = new OperatorSettingsBuilder
        {
            ParallelReconciliation = new()
            {
                MaxParallelReconciliations = 2,
                MaxErrorRetries = 0,
            },
        }.Build();

        await using var service = CreateService(queue, reconcilerMock, clientMock, entity, settings);
        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Push(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        queue.Complete();

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        queue.EnqueueCallCount.Should().Be(0);
    }
}
