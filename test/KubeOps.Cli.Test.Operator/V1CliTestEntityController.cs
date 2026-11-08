// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;

namespace KubeOps.Cli.Test.Operator;

[EntityRbac(typeof(V1CliTestEntity), Verbs = RbacVerb.All)]
public sealed class V1CliTestEntityController : IEntityController<V1CliTestEntity>
{
    public Task<ReconciliationResult<V1CliTestEntity>> ReconcileAsync(
        V1CliTestEntity entity,
        CancellationToken cancellationToken)
        => Task.FromResult(ReconciliationResult<V1CliTestEntity>.Success(entity));

    public Task<ReconciliationResult<V1CliTestEntity>> DeletedAsync(
        V1CliTestEntity entity,
        CancellationToken cancellationToken)
        => Task.FromResult(ReconciliationResult<V1CliTestEntity>.Success(entity));
}
