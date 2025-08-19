// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.LeaderElection;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.LeaderElection;

/// <summary>
/// This background service connects to the API and continuously watches the leader election.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="elector">The elector.</param>
internal sealed class LeaderElectionBackgroundService(ILogger<LeaderElectionBackgroundService> logger, LeaderElector elector)
    : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private Task? _leadershipTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The current implementation of IHostedService expects that StartAsync is "really" asynchronous.
        // Blocking calls are not allowed, they would stop the rest of the startup flow.
        //
        // This is an open issue since 2019 and not expected to be closed soon. (https://github.com/dotnet/runtime/issues/36063)
        // For reasons unknown at the time of writing this code, "await Task.Yield()" didn't work as expected, it caused
        // a deadlock in 1/10 of the cases.
        //
        // Therefore, we use Task.Run() and put the work to queue. The passed cancellation token of the StartAsync
        // method is not used, because it would only cancel the scheduling (which we definitely don't want to cancel).
        // To make this intention explicit, CancellationToken.None gets passed.
        _leadershipTask = Task.Run(RunAndTryToHoldLeadershipForeverAsync, CancellationToken.None);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts.Dispose();
        elector.Dispose();
        _disposed = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _cts.CancelAsync();

        if (_leadershipTask is not null)
        {
            await _leadershipTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CastAndDispose(_cts);
        await CastAndDispose(elector);

        _disposed = true;

        static async ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
            {
                await resourceAsyncDisposable.DisposeAsync();
            }
            else
            {
                resource.Dispose();
            }
        }
    }

    private async Task RunAndTryToHoldLeadershipForeverAsync()
    {
        uint leadershipRetries = 0;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await elector.RunUntilLeadershipLostAsync(_cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // Ignore cancellation exceptions when we've been asked to stop.
            }
            catch (Exception exception)
            {
                leadershipRetries++;

                var delay = TimeSpan
                    .FromSeconds(Math.Pow(2, Math.Clamp(leadershipRetries, 0, 5)))
                    .Add(TimeSpan.FromMilliseconds(new Random().Next(0, 1000)));

                logger.LogError(exception, "Failed to hold leadership. Wait {Seconds}s before attempting to reacquire leadership.", delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
    }
}
