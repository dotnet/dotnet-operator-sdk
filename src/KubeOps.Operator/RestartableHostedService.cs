// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

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
/// <see cref="StopAsync"/> only requests cancellation and does not block on the drain — a leadership callback may
/// fire-and-forget it. A leadership flap (stop then start) can therefore briefly leave the previous loop draining
/// while the next loop is already running; both are tracked, and disposal drains <strong>all</strong> of them
/// (bounded by <see cref="DrainGracePeriod"/>) before subclass resources are released, so no in-flight work can
/// touch an already-disposed resource. A reconciler that ignores its <see cref="CancellationToken"/> cannot block
/// disposal beyond the grace period.
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

    /// <summary>Gets a value indicating whether the service has been disposed.</summary>
    protected bool IsDisposed => _disposed;

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
    public virtual Task StopAsync(CancellationToken cancellationToken)
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

        // Non-blocking: only request cancellation. Disposal drains all loops before resources are released.
        return CancelRunsAsync(runs);
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
    /// Runs one iteration of the background loop. Implementations must honor <paramref name="cancellationToken"/>
    /// and return when it is cancelled.
    /// </summary>
    /// <param name="cancellationToken">A token signalled when the loop should stop.</param>
    /// <returns>A task that completes when the loop has stopped.</returns>
    protected abstract Task ExecuteAsync(CancellationToken cancellationToken);

    /// <summary>Releases subclass-managed resources synchronously. Called from <see cref="Dispose(bool)"/>.</summary>
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
            runs = [.. _activeRuns];
        }

        await DrainRunsAsync(runs, CancellationToken.None);
        await DisposeManagedResourcesAsync();

        _disposed = true;
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
    // so StartAsync never disposes a token source that this loop is still using.
    private async Task RunLoopAsync(CancellationTokenSource cts)
    {
        try
        {
            await ExecuteAsync(cts.Token);
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _activeRuns.RemoveAll(r => ReferenceEquals(r.Cts, cts));
            }

            cts.Dispose();
        }
    }
}
