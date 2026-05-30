// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using FluentAssertions;

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

    private static TestableService CreateService(CapturingQueue queue)
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

        return new(queue, elector);
    }

    /// <summary>
    /// Exposes the private leadership callbacks for testing by invoking the delegates registered on the
    /// elector, mirroring the approach used in the leader-aware resource watcher tests.
    /// </summary>
    private sealed class TestableService : LeaderAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>
    {
        private readonly LeaderElectorType _elector;

        public TestableService(CapturingQueue queue, LeaderElectorType elector)
            : base(
                new("test"),
                Mock.Of<IKubernetesClient>(),
                new OperatorSettingsBuilder { Namespace = "unit-test" }.Build(),
                queue,
                Mock.Of<IReconciler<V1OperatorIntegrationTestEntity>>(),
                Mock.Of<ILogger<LeaderAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>>>(),
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

    private sealed class CapturingQueue : ITimedEntityQueue<V1OperatorIntegrationTestEntity>
    {
        private readonly TaskCompletionSource _enumeratorStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _enumeratorCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CapturedToken { get; private set; }

        public Task EnumeratorStarted => _enumeratorStarted.Task;

        public Task EnumeratorCancelled => _enumeratorCancelled.Task;

        public Task Enqueue(
            V1OperatorIntegrationTestEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken) => Task.CompletedTask;

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
}
