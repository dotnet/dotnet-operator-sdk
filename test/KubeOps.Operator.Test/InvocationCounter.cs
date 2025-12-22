// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using k8s;
using k8s.Models;

namespace KubeOps.Operator.Test;

public sealed class InvocationCounter<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private TaskCompletionSource _task = new();
    private readonly ConcurrentQueue<(string Method, TEntity Entity)> _invocations = new();
    public IReadOnlyList<(string Method, TEntity Entity)> Invocations => _invocations.ToList();

#if DEBUG
    public Task WaitForInvocations => _task.Task;
#else
    public Task WaitForInvocations => _task.Task.WaitAsync(TimeSpan.FromSeconds(30));
#endif

    public int TargetInvocationCount { get; set; } = 1;

    public void Invocation(TEntity entity, [CallerMemberName] string name = "Invocation")
    {
        _invocations.Enqueue((name, entity));
        if (Invocations.Count >= TargetInvocationCount)
        {
            _task.TrySetResult();
        }
    }

    public void Clear()
    {
        _invocations.Clear();
        _task = new TaskCompletionSource();
        TargetInvocationCount = 1;
    }
}
