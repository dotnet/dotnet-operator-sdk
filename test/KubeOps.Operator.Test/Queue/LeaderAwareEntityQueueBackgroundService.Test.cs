// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.Logging;

using Moq;

using ILock = k8s.LeaderElection.ILock;
using LeaderElectorType = k8s.LeaderElection.LeaderElector;

namespace KubeOps.Operator.Test.Queue;

[Trait("Area", "LeaderLoss")]
public sealed class LeaderAwareEntityQueueBackgroundServiceTest
{
    [Fact]
    public async Task StartedLeading_Should_Begin_Consuming_Queue()
    {
        var queue = new CapturingQueue();
        await using var service = CreateService(queue);

        await service.StartAsync(TestContext.Current.CancellationToken);
        service.SimulateStartedLeading();

        await queue.EnumeratorStarted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        queue.CapturedToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task StoppedLeading_Should_Hard_Stop_Queue_Processing()
    {
        var queue = new CapturingQueue();
        await using var service = CreateService(queue);

        await service.StartAsync(TestContext.Current.CancellationToken);
        service.SimulateStartedLeading();
        await queue.EnumeratorStarted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        service.SimulateStoppedLeading();

        // Cancelling the internal token must abort the in-flight queue consumption.
        await queue.EnumeratorCancelled.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        queue.CapturedToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task StartedLeading_Should_Resume_Intake_And_StoppedLeading_Should_Suspend_Then_Clear()
    {
        var queue = new CapturingQueue();
        await using var service = CreateService(queue);

        // StartAsync subscribes to the elector; not leading yet, so it closes the gate first.
        await service.StartAsync(TestContext.Current.CancellationToken);
        queue.GateCalls.Should().Equal("SuspendIntake");
        queue.GateCalls.Clear();

        service.SimulateStartedLeading();
        service.SimulateStoppedLeading();

        // ResumeIntake happens before producing; on stop the gate is closed (SuspendIntake) before the
        // queue is cleared, so no in-flight requeue can slip in between the clear and the gate closing.
        queue.GateCalls.Should().Equal("ResumeIntake", "SuspendIntake", "Clear");
    }

    [Fact]
    public async Task StoppedLeading_Should_Clear_Queue_And_Drop_Later_Requeues()
    {
        using var realQueue = new TimedEntityQueue<V1OperatorIntegrationTestEntity>(
            Mock.Of<ILogger<TimedEntityQueue<V1OperatorIntegrationTestEntity>>>());
        await using var service = CreateService(realQueue);

        await service.StartAsync(TestContext.Current.CancellationToken);
        service.SimulateStartedLeading();

        // Work the former leader scheduled before losing leadership.
        await realQueue.Enqueue(
            CreateEntity("to-clear"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.FromSeconds(5),
            retryCount: 0,
            TestContext.Current.CancellationToken);
        realQueue.Count.Should().Be(1);

        service.SimulateStoppedLeading();

        // The pre-stop entry is cleared.
        realQueue.Count.Should().Be(0);
        realQueue.ReadyCount.Should().Be(0);

        // Simulate an in-flight reconciler finishing AFTER leadership loss and returning
        // Success(entity, requeueAfter): the requeue must be dropped, leaving no stale work behind.
        await realQueue.Enqueue(
            CreateEntity("late-requeue"),
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.Zero,
            retryCount: 0,
            TestContext.Current.CancellationToken);

        realQueue.Count.Should().Be(0);
        realQueue.ReadyCount.Should().Be(0);
    }

    [Fact]
    public async Task Queue_Without_Suspendable_Capability_Logs_Warning_And_Degrades_Gracefully()
    {
        // A custom queue that does not implement ISuspendableEntityQueue must not break leadership
        // transitions; gating is skipped and a warning is logged so the missing protection is visible.
        var loggerMock = new Mock<ILogger<LeaderAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>>>();
        var queue = new NonSuspendableQueue();
        await using var service = CreateService(queue, loggerMock.Object);

        await service.StartAsync(TestContext.Current.CancellationToken);

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((@object, _) => @object.ToString()!.Contains(nameof(ISuspendableEntityQueue))),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        service.SimulateStartedLeading();
        var act = service.SimulateStoppedLeading;
        act.Should().NotThrow();
    }

    private static V1OperatorIntegrationTestEntity CreateEntity(string name)
    {
        var entity = new V1OperatorIntegrationTestEntity();
        entity.EnsureMetadata();
        entity.Metadata.SetNamespace("unit-test");
        entity.Metadata.Name = name;
        return entity;
    }

    private static TestableService CreateService(
        ITimedEntityQueue<V1OperatorIntegrationTestEntity> queue,
        ILogger<LeaderAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>>? logger = null)
    {
        var lockMock = new Mock<ILock>();
        lockMock
            .Setup(l => l.GetAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct => { await Task.Delay(Timeout.Infinite, ct); return null!; });

        var elector = new LeaderElectorType(new(lockMock.Object)
        {
            LeaseDuration = TimeSpan.FromSeconds(1),
            RenewDeadline = TimeSpan.FromMilliseconds(500),
            RetryPeriod = TimeSpan.FromMilliseconds(100),
        });

        return new(
            queue,
            elector,
            logger ?? Mock.Of<ILogger<LeaderAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>>>());
    }

    /// <summary>
    /// Exposes the private leadership callbacks for testing by invoking the delegates registered on the
    /// elector, mirroring the approach used in the leader-aware resource watcher tests.
    /// </summary>
    private sealed class TestableService : LeaderAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>
    {
        private readonly LeaderElectorType _elector;

        public TestableService(
            ITimedEntityQueue<V1OperatorIntegrationTestEntity> queue,
            LeaderElectorType elector,
            ILogger<LeaderAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>> logger)
            : base(
                new("test"),
                Mock.Of<IKubernetesClient>(),
                new OperatorSettingsBuilder { Namespace = "unit-test" }.Build(),
                queue,
                Mock.Of<IReconciler<V1OperatorIntegrationTestEntity>>(),
                logger,
                elector)
        {
            _elector = elector;
        }

        public void SimulateStartedLeading() => InvokeElectorEvent(nameof(LeaderElectorType.OnStartedLeading));

        public void SimulateStoppedLeading() => InvokeElectorEvent(nameof(LeaderElectorType.OnStoppedLeading));

        private void InvokeElectorEvent(string eventName)
        {
            var field = typeof(LeaderElectorType)
                .GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            var handler = (Action?)field?.GetValue(_elector);
            handler?.Invoke();
        }
    }

    private sealed class CapturingQueue : ITimedEntityQueue<V1OperatorIntegrationTestEntity>, ISuspendableEntityQueue
    {
        private readonly TaskCompletionSource _enumeratorStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _enumeratorCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CapturedToken { get; private set; }

        public Task EnumeratorStarted => _enumeratorStarted.Task;

        public Task EnumeratorCancelled => _enumeratorCancelled.Task;

        // Records the order of intake/clear calls so tests can assert the leadership-transition sequence.
        public List<string> GateCalls { get; } = [];

        public Task<bool> Enqueue(
            V1OperatorIntegrationTestEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public void Clear() => GateCalls.Add(nameof(Clear));

        public void SuspendIntake() => GateCalls.Add(nameof(SuspendIntake));

        public void ResumeIntake() => GateCalls.Add(nameof(ResumeIntake));

        public async IAsyncEnumerator<QueueEntry<V1OperatorIntegrationTestEntity>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            CapturedToken = cancellationToken;
            var blocker = new TaskCompletionSource();
            await using var registration = cancellationToken.Register(() =>
            {
                blocker.TrySetResult();
                _enumeratorCancelled.TrySetResult();
            });

            _enumeratorStarted.TrySetResult();
            await blocker.Task;
            yield break;
        }

        public void Dispose()
        {
        }
    }

    // A custom queue that deliberately does NOT implement ISuspendableEntityQueue, to verify the
    // leader-aware consumer degrades gracefully (skips gating) instead of failing.
    private sealed class NonSuspendableQueue : ITimedEntityQueue<V1OperatorIntegrationTestEntity>
    {
        public Task<bool> Enqueue(
            V1OperatorIntegrationTestEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public async IAsyncEnumerator<QueueEntry<V1OperatorIntegrationTestEntity>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }

        public void Dispose()
        {
        }
    }
}
