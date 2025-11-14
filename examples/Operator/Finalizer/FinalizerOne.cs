// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Finalizer;

using Operator.Entities;

namespace Operator.Finalizer;

public sealed class FinalizerOne : IEntityFinalizer<V1TestEntity>
{
    public Task<ReconciliationResult<V1TestEntity>> FinalizeAsync(V1TestEntity entity, CancellationToken cancellationToken)
        => Task.FromResult(ReconciliationResult<V1TestEntity>.Success(entity));
}
