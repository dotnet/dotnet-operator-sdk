// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using FluentAssertions;

namespace KubeOps.Operator.Test;

public sealed class RestartableHostedServiceTest
{
    [Fact]
    public async Task Faulted_Loop_Is_Restarted_With_Backoff()
    {
        // The loop body throws on its first run; the service must restart it (immediate back-off in the test)
        // instead of leaving it dead for the rest of the process lifetime.
        var secondRunStarted = new TaskCompletionSource();
        await using var service = new TestRestartableHostedService((count, ct) =>
        {
            if (count == 1)
            {
                throw new InvalidOperationException("boom");
            }

            secondRunStarted.TrySetResult();
            return Task.Delay(Timeout.Infinite, ct);
        })
        {
            FaultBackoff = _ => TimeSpan.Zero,
            DrainGracePeriod = TimeSpan.FromSeconds(5),
        };

        await service.StartAsync(TestContext.Current.CancellationToken);

        var restarted = await Task.WhenAny(
            secondRunStarted.Task,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        restarted.Should().Be(secondRunStarted.Task, "the faulted loop should be restarted");
        service.FaultCount.Should().Be(1);

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Clean_Return_Is_Not_Restarted()
    {
        // A clean ExecuteAsync return means the loop is done; it must not be treated as a fault and restarted.
        await using var service = new TestRestartableHostedService((_, _) => Task.CompletedTask);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        service.ExecuteCount.Should().Be(1);
        service.FaultCount.Should().Be(0);

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_During_Disposal_Does_Not_Start_A_New_Loop()
    {
        // Simulates a leadership callback racing disposal: a StartAsync triggered while disposing (here from
        // OnDisposing) must be suppressed so no new loop escapes the drain and runs on torn-down resources.
        var service = new TestRestartableHostedService((_, ct) => Task.Delay(Timeout.Infinite, ct))
        {
            DrainGracePeriod = TimeSpan.FromSeconds(5),
        };

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => service.ExecuteCount == 1);

        service.OnDisposingHook = () => service.StartAsync(CancellationToken.None);
        await service.DisposeAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        service.ExecuteCount.Should().Be(1, "a StartAsync during disposal must not start a new loop");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition() && deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue("the awaited condition did not become true in time");
    }

    private sealed class TestRestartableHostedService(Func<int, CancellationToken, Task> body)
        : RestartableHostedService
    {
        private int _executeCount;
        private int _faultCount;

        public int ExecuteCount => Volatile.Read(ref _executeCount);

        public int FaultCount => Volatile.Read(ref _faultCount);

        public Action? OnDisposingHook { get; set; }

        protected override Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _executeCount);
            return body(count, cancellationToken);
        }

        protected override void OnLoopFaulted(Exception exception) => Interlocked.Increment(ref _faultCount);

        protected override void OnDisposing() => OnDisposingHook?.Invoke();
    }
}
