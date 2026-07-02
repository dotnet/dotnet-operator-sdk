// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using FluentAssertions;

namespace KubeOps.Operator.Test;

[Trait("Area", "LeaderLoss")]
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
    public async Task FaultBackoff_Counter_Escalates_For_A_Tight_Crash_Loop()
    {
        // With a large reset threshold (default) and instantly-faulting runs, the back-off counter must keep
        // escalating: consecutive faults pass 1, 2, 3, ... so the delay grows.
        var retryCounts = new List<uint>();
        var enough = new TaskCompletionSource();
        await using var service = new TestRestartableHostedService((_, _) => throw new InvalidOperationException("boom"))
        {
            FaultBackoff = n =>
            {
                retryCounts.Add(n);
                if (retryCounts.Count >= 3)
                {
                    enough.TrySetResult();
                }

                return TimeSpan.Zero;
            },
            FaultBackoffResetThreshold = TimeSpan.FromMinutes(1),
            DrainGracePeriod = TimeSpan.FromSeconds(5),
        };

        await service.StartAsync(TestContext.Current.CancellationToken);
        (await Task.WhenAny(enough.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)))
            .Should().Be(enough.Task);
        await service.StopAsync(TestContext.Current.CancellationToken);

        retryCounts.Take(3).Should().Equal(1u, 2u, 3u);
    }

    [Fact]
    public async Task FaultBackoff_Counter_Resets_After_A_Healthy_Run()
    {
        // With a zero reset threshold every run counts as "healthy", so each fault is treated as fresh and the
        // back-off counter restarts at 1 instead of escalating.
        var retryCounts = new List<uint>();
        var enough = new TaskCompletionSource();
        await using var service = new TestRestartableHostedService((_, _) => throw new InvalidOperationException("boom"))
        {
            FaultBackoff = n =>
            {
                retryCounts.Add(n);
                if (retryCounts.Count >= 3)
                {
                    enough.TrySetResult();
                }

                return TimeSpan.Zero;
            },
            FaultBackoffResetThreshold = TimeSpan.Zero,
            DrainGracePeriod = TimeSpan.FromSeconds(5),
        };

        await service.StartAsync(TestContext.Current.CancellationToken);
        (await Task.WhenAny(enough.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)))
            .Should().Be(enough.Task);
        await service.StopAsync(TestContext.Current.CancellationToken);

        retryCounts.Take(3).Should().Equal(1u, 1u, 1u);
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

    [Fact]
    public async Task Flap_Starts_New_Loop_While_Previous_Is_Still_Draining_And_Disposal_Drains_Both()
    {
        // A leadership flap: request-stop (cancel the current loop) then start a new one before the previous loop
        // has finished unwinding. Both runs must be tracked — the new loop runs while the old one drains — and a
        // later disposal must drain both cleanly (no hang, no fault). Each loop owns its own CancellationTokenSource,
        // so the restart must not dispose the source the still-draining former loop is observing.
        var run1Started = new TaskCompletionSource();
        var run1Drained = new TaskCompletionSource();
        var run2Started = new TaskCompletionSource();
        var drainGate1 = new TaskCompletionSource();

        await using var service = new TestRestartableHostedService(async (count, ct) =>
        {
            if (count == 1)
            {
                run1Started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    // Linger to simulate a slow drain: stay alive until the test releases the gate, so the new
                    // loop provably runs concurrently with this one still unwinding.
                    await drainGate1.Task;
                    run1Drained.TrySetResult();
                    throw;
                }
            }
            else
            {
                run2Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        })
        {
            DrainGracePeriod = TimeSpan.FromSeconds(5),
        };

        // Loop 1 running.
        await service.StartAsync(TestContext.Current.CancellationToken);
        (await Task.WhenAny(run1Started.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)))
            .Should().Be(run1Started.Task);

        // Flap: cancel loop 1 (it lingers on the gate), then immediately start loop 2.
        await service.RequestStopForTest();
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Loop 2 runs while loop 1 is still draining (gate not yet released).
        (await Task.WhenAny(run2Started.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)))
            .Should().Be(run2Started.Task, "the new loop should start while the previous one is still draining");
        run1Drained.Task.IsCompleted.Should().BeFalse("loop 1 should still be draining during the overlap");
        service.ExecuteCount.Should().Be(2, "exactly one new loop should have been started by the flap");

        // Release loop 1's drain; it must unwind cleanly.
        drainGate1.TrySetResult();
        (await Task.WhenAny(run1Drained.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)))
            .Should().Be(run1Drained.Task);

        // Disposal drains loop 2 (and any residue of loop 1) without hanging or faulting.
        await service.DisposeAsync();

        service.ExecuteCount.Should().Be(2);
        service.FaultCount.Should().Be(0, "cancellation is not a fault");
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

        // Exposes the protected, non-blocking stop so a test can trigger a flap (request-stop then start).
        public Task RequestStopForTest() => RequestStopAsync();

        protected override Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _executeCount);
            return body(count, cancellationToken);
        }

        protected override void OnLoopFaulted(Exception exception) => Interlocked.Increment(ref _faultCount);

        protected override void OnDisposing() => OnDisposingHook?.Invoke();
    }
}
