// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Queue;

namespace KubeOps.Operator.Queue;

public sealed record RequeueEntry<TEntity>
{
    private RequeueEntry(TEntity entity, RequeueType requeueType)
    {
        Entity = entity;
        RequeueType = requeueType;
    }

    public TEntity Entity { get; }

    public RequeueType RequeueType { get; }

    public static RequeueEntry<TEntity> CreateFor(TEntity entity, RequeueType requeueType)
        => new(entity, requeueType);
}
