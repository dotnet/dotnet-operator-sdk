// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using KubeOps.Operator.Retry;

using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator;

/// <summary>
/// Base class for a hosted service that runs a single, restartable background loop and drains all in-flight work
/// before its resources are disposed. It is the shared lifecycle for the operator's leadership-aware background
/// services (the queue consumer and the resource watcher).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StartAsync"/> is <strong>idempotent</strong>: while a loop is already running it does nothing, so a
/// race where two callers start the loop (for example a leadership-aware <c>StartAsync</c> and a concurrent
/// <c>OnStartedLeading</c> callback) can never start two loops. Each loop owns its own
/// <see cref="CancellationTokenSource"/> and disposes it only when the loop has finished, so a restart never
/// disposes a token source a still-running former loop is still observing.
/// </para>
/// <para>
/// There are two ways to stop the loop, with deliberately different semantics:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="StopAsync"/> — the host-shutdown entry point. It requests cancellation and then <strong>awaits the
/// drain</strong> of all in-flight work, bounded by its cancellation token (the host shutdown deadline) and
/// <see cref="DrainGracePeriod"/>. This honors the <see cref="IHostedService"/> contract that a graceful host stop
/// waits for the service to stop.
/// </description></item>
/// <item><description>
/// <see cref="RequestStopAsync"/> — requests cancellation <strong>without waiting</strong>, for callers that must
/// not block (such as a leadership-loss callback). On leadership loss the in-flight reconciliation is cancelled and
/// the operator moves on — it does <strong>not</strong> wait for it to finish — which matches controller-runtime /
/// other operator SDKs. (KubeOps does not terminate the process on leadership loss, so cancellation is cooperative
/// rather than a hard <c>os.Exit</c>.)
/// </description></item>
/// </list>
/// <para>
/// "Draining" never means letting in-flight work <em>complete</em>: the work is cancelled first, and the bounded
/// wait only lets the already-cancelled loop <em>unwind</em> so that shared resources are not torn down underneath
/// a still-running worker. A flap (request-stop then start) can briefly leave the previous loop draining while the
/// next loop already runs; both are tracked, and disposal drains <strong>all</strong> of them (bounded by
/// <see cref="DrainGracePeriod"/>) before subclass resources are released. A reconciler that ignores its
/// <see cref="CancellationToken"/> cannot block shutdown beyond the grace period.
/// </para>
/// </remarks>
public abstract class RestartableHostedService : IHostedService, IDisposable, IAsyncDisposable
{
    // Guards the start/stop lifecycle: _running (idempotency gate for the current term) and _activeRuns.
    private readonly object _lifecycleLock = new();

    // Every loop that has been started and not yet finished, with the token source it owns. Normally one, but a
    // flap (stop -> start) can briefly leave the previous loop still draining while the next one runs. Disposal
    // drains them ALL.
    private readonly List<(Task Loop, CancellationTokenSource Cts)> _activeRuns = [];

    private bool _running;
    private volatile bool _disposed;

    /// <summary>
    /// Bounds how long a dispose waits for in-flight work to drain. A non-cooperative loop body that ignores its
    /// <see cref="CancellationToken"/> cannot block shutdown beyond this. Internal so tests can shorten it.
    /// </summary>
    internal TimeSpan DrainGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Computes the back-off delay before a faulted loop is restarted, from the consecutive fault count.
    /// Defaults to an exponential back-off with jitter; internal so tests can make restarts immediate.
    /// </summary>
    internal Func<uint, TimeSpan> FaultBackoff { get; set; } = ExponentialRetryBackoff.GetDelayWithJitter;

    /// <summary>
    /// How long a loop iteration must run before a subsequent fault is treated as a fresh failure and the
    /// back-off counter is reset to zero. This keeps the back-off escalating for a tight crash loop while not
    /// penalising an isolated fault that occurs after a long healthy run. Must exceed the maximum back-off.
    /// Internal so tests can control it.
    /// </summary>
    internal TimeSpan FaultBackoffResetThreshold { get; set; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc cref="IHostedService.StartAsync"/>
    /// <remarks>
    /// Idempotent: starts the background loop only if one is not already running. The loop is scheduled with
    /// <see cref="Task.Run(Func{Task}, CancellationToken)"/>; the <paramref name="cancellationToken"/> is not used
    /// for the loop itself (it would only cancel host startup scheduling) — the loop is cancelled via
    /// <see cref="StopAsync"/> / disposal.
    /// </remarks>
    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_disposed || _running)
            {
                return Task.CompletedTask;
            }

            var cts = new CancellationTokenSource();
            _running = true;
            var loop = Task.Run(() => RunLoopAsync(cts), CancellationToken.None);
            _activeRuns.Add((loop, cts));

            return Task.CompletedTask;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Honors the <see cref="IHostedService"/> contract: it requests the loop to stop and then <strong>awaits the
    /// drain</strong> of all in-flight work, bounded by <paramref name="cancellationToken"/> (the host shutdown
    /// deadline) and <see cref="DrainGracePeriod"/>. It drains whatever is still active even if a prior
    /// <see cref="RequestStopAsync"/> already cleared the running state. For a non-blocking stop (e.g. from a
    /// leadership-loss callback) use <see cref="RequestStopAsync"/>.
    /// </remarks>
    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        (Task Loop, CancellationTokenSource Cts)[] runs;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _running = false;
            runs = [.. _activeRuns];
        }

        await DrainRunsAsync(runs, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes a resource, preferring its asynchronous disposal when available.</summary>
    /// <param name="resource">The resource to dispose.</param>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    protected static async ValueTask CastAndDisposeAsync(IDisposable resource)
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

    /// <summary>
    /// Requests the loop to stop without waiting for it to drain — for callers that must not block, such as a
    /// leadership-loss callback. The remaining drain is awaited by the next <see cref="StopAsync"/> or by disposal.
    /// </summary>
    /// <returns>A task that completes once cancellation has been signalled (not when the loop has drained).</returns>
    protected Task RequestStopAsync()
    {
        (Task Loop, CancellationTokenSource Cts)[] runs;
        lock (_lifecycleLock)
        {
            if (_disposed || !_running)
            {
                return Task.CompletedTask;
            }

            // Clear the desired-running state so a subsequent StartAsync (e.g. on re-acquired leadership) starts a
            // fresh loop instead of being suppressed by the idempotency guard.
            _running = false;
            runs = [.. _activeRuns];
        }

        return CancelRunsAsync(runs);
    }

    /// <summary>
    /// Runs one iteration of the background loop. Implementations must honor <paramref name="cancellationToken"/>
    /// and return when it is cancelled.
    /// </summary>
    /// <param name="cancellationToken">A token signalled when the loop should stop.</param>
    /// <returns>A task that completes when the loop has stopped.</returns>
    protected abstract Task ExecuteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Called each time <see cref="ExecuteAsync"/> exits with an unexpected error (any exception other than its own
    /// cancellation), before the loop is restarted with a back-off (see <see cref="FaultBackoff"/>). Override to log
    /// it so a faulting loop is visible. Defaults to no-op.
    /// </summary>
    /// <param name="exception">The exception the loop exited with.</param>
    protected virtual void OnLoopFaulted(Exception exception)
    {
    }

    /// <summary>
    /// Releases subclass-managed resources synchronously. Called from <see cref="Dispose(bool)"/>.
    /// </summary>
    protected virtual void DisposeManagedResources()
    {
    }

    /// <summary>
    /// Releases subclass-managed resources asynchronously, after all loops have been drained. Called from
    /// <see cref="DisposeAsyncCore"/>. Defaults to <see cref="DisposeManagedResources"/>.
    /// </summary>
    /// <returns>A task that represents the asynchronous release operation.</returns>
    protected virtual ValueTask DisposeManagedResourcesAsync()
    {
        DisposeManagedResources();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Cleanup that must run <strong>before</strong> the loops are drained — for example unsubscribing event handlers
    /// so that no leadership callback can start a new loop while disposal is in progress. Runs on both the synchronous
    /// (<see cref="Dispose(bool)"/>) and asynchronous (<see cref="DisposeAsyncCore"/>) disposal paths, so subclasses
    /// do not have to override both dispose methods to be safe.
    /// </summary>
    protected virtual void OnDisposing()
    {
    }

    /// <summary>Releases the resources used by the service.</summary>
    /// <param name="disposing">Whether the method is called from <see cref="Dispose()"/>.</param>
    /// <remarks>
    /// The synchronous path cannot await the loops to drain (see <see cref="DisposeAsyncCore"/> for the draining
    /// path); the container disposes via <see cref="IAsyncDisposable"/> when available, so this is the best-effort
    /// fallback.
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        OnDisposing();
        DisposeManagedResources();
        _disposed = true;
    }

    /// <summary>Asynchronously drains all loops and releases the resources used by the service.</summary>
    /// <remarks>
    /// Overriding subclasses must call <c>base.DisposeAsyncCore()</c> so the loops are drained and resources are
    /// released on the asynchronous disposal path too.
    /// </remarks>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        (Task Loop, CancellationTokenSource Cts)[] runs;
        lock (_lifecycleLock)
        {
            _running = false;

            // Mark disposed under the lock and before the drain so a leadership callback racing the drain is
            // suppressed by StartAsync's guard and cannot add a new loop that would escape the snapshot below.
            _disposed = true;
            runs = [.. _activeRuns];
        }

        // Unsubscribe before draining so no leadership callback can start a fresh loop during the drain.
        OnDisposing();
        await DrainRunsAsync(runs, CancellationToken.None);
        await DisposeManagedResourcesAsync();
    }

    private static async Task CancelRunsAsync(IReadOnlyCollection<(Task Loop, CancellationTokenSource Cts)> runs)
    {
        foreach (var (_, cts) in runs)
        {
            try
            {
                await cts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // The loop already finished and disposed its own token source; nothing to cancel.
            }
        }
    }

    private async Task DrainRunsAsync(
        IReadOnlyCollection<(Task Loop, CancellationTokenSource Cts)> runs, CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return;
        }

        await CancelRunsAsync(runs);

        try
        {
            // Bound the wait so a non-cooperative loop body cannot block shutdown indefinitely; after the grace
            // elapses we proceed (documented limitation).
            await Task.WhenAll(runs.Select(r => r.Loop)).WaitAsync(DrainGracePeriod, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Grace elapsed while work was still running; proceed with disposal.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown deadline reached; proceed.
        }
    }

    // Runs one loop and owns its CancellationTokenSource: it disposes the source only after the loop has finished,
    // so StartAsync never disposes a token source that this loop is still using. A loop that faults (any exception
    // other than its own cancellation) is restarted in place with a back-off, so a single transient error cannot
    // silently stop a service for the rest of the process lifetime; only cancellation or a clean return ends it.
    private async Task RunLoopAsync(CancellationTokenSource cts)
    {
        uint faultRetries = 0;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var startedAt = Stopwatch.GetTimestamp();
                try
                {
                    await ExecuteAsync(cts.Token);

                    // ExecuteAsync returned without being cancelled: the loop body decided it is done. A clean
                    // return is not a fault, so it is not restarted.
                    break;
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    // Expected: the loop was cancelled by a stop or disposal. Nothing to report.
                    break;
                }
                catch (Exception exception)
                {
                    // The loop exited with an unexpected error (not its own cancellation). Surface it, then wait a
                    // back-off and restart so the service keeps working after a transient failure.
                    OnLoopFaulted(exception);

                    // If the loop ran healthily for a while before faulting, treat this as a fresh failure and
                    // restart the back-off from the beginning, so an isolated late fault is not penalised by
                    // earlier, already-recovered ones. A tight crash loop keeps escalating.
                    if (Stopwatch.GetElapsedTime(startedAt) >= FaultBackoffResetThreshold)
                    {
                        faultRetries = 0;
                    }

                    try
                    {
                        await Task.Delay(FaultBackoff(++faultRetries), cts.Token);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _activeRuns.RemoveAll(r => ReferenceEquals(r.Cts, cts));

                // If no loop remains while we still think we are running, the loop exited without a stop request
                // (ExecuteAsync returned cleanly). Reset so a subsequent StartAsync can restart it instead of
                // being suppressed forever by the idempotency guard.
                if (_activeRuns.Count == 0)
                {
                    _running = false;
                }
            }

            cts.Dispose();
        }
    }
}
