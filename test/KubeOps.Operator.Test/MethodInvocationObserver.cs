// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using k8s;
using k8s.Models;

using Xunit.Sdk;

namespace KubeOps.Operator.Test;

/// <summary>
/// Observes method invocations on a controller and allows tests to wait for a specific
/// method to be called.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type being observed.</typeparam>
public sealed class MethodInvocationObserver<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly ConcurrentQueue<(string Method, TEntity Entity)> _invocations = new();
    private volatile TaskCompletionSource? _pendingWait;
    private string? _waitForMethod;

    /// <summary>
    /// Gets all invocations recorded so far, in the order they occurred.
    /// Each entry contains the method name and the entity that was passed to it.
    /// </summary>
    public IReadOnlyList<(string Method, TEntity Entity)> Invocations => _invocations.ToList();

    /// <summary>
    /// Configures the observer to signal completion when the specified method is invoked
    /// and returns a task that completes at that point.
    /// Must be called before the operation that triggers the method to avoid race conditions.
    /// The task fails with an <see cref="XunitException"/> if the
    /// <paramref name="cancellationToken"/> is cancelled before the method is invoked,
    /// showing all invocations recorded up to that point.
    /// </summary>
    /// <param name="methodName">
    /// The name of the method to wait for. Use <see langword="nameof"/> to avoid magic strings.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token from <c>TestContext.Current.CancellationToken</c>.
    /// </param>
    /// <returns>A task that completes when the specified method is invoked.</returns>
    /// <example>
    /// <code>
    /// var waitTask = observer.WaitForMethod(
    ///     nameof(IEntityController&lt;T&gt;.DeletedAsync),
    ///     TestContext.Current.CancellationToken);
    ///
    /// await client.DeleteAsync(entity);
    /// await waitTask;
    /// </code>
    /// </example>
    public Task WaitForMethod(string methodName, CancellationToken cancellationToken)
    {
        _waitForMethod = methodName;
        _pendingWait = new(TaskCreationOptions.RunContinuationsAsynchronously);

        return WaitWithCancellation(_pendingWait.Task, methodName, cancellationToken);
    }

    /// <summary>
    /// Records an invocation of the calling method.
    /// </summary>
    /// <param name="entity">The entity passed to the controller method.</param>
    /// <param name="name">The name of the calling method, injected by the compiler.</param>
    public void RecordInvocation(TEntity entity, [CallerMemberName] string name = "Invocation")
    {
        _invocations.Enqueue((name, entity));

        if (_pendingWait is { } tcs && name == _waitForMethod)
        {
            tcs.TrySetResult();
        }
    }

    /// <summary>
    /// Clears all recorded method invocations and resets the observer's internal state.
    /// This method removes all pending invocations in the queue, cancels any
    /// ongoing wait operations, and sets the observer to its initial state.
    /// </summary>
    public void Clear()
    {
        _invocations.Clear();
        _pendingWait = null;
        _waitForMethod = null;
    }

    /// <summary>
    /// Awaits the given task with cancellation support. On cancellation, throws an
    /// <see cref="XunitException"/> instead of <see cref="OperationCanceledException"/>
    /// so that xUnit marks the test as failed and shows the already-recorded invocations
    /// as diagnostic context.
    /// </summary>
    private async Task WaitWithCancellation(Task task, string methodName, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var recorded = _invocations
                .Select((inv, i) => $"  [{i + 1}] {inv.Method}({inv.Entity.Name()}/{inv.Entity.Namespace()})")
                .ToList();

            var invocationList = recorded.Count > 0
                ? string.Join(Environment.NewLine, recorded)
                : "  (none)";

            throw new XunitException(
                $"Timed out waiting for '{methodName}'." +
                $"{Environment.NewLine}Recorded invocations ({_invocations.Count}):" +
                $"{Environment.NewLine}{invocationList}");
        }
    }
}
