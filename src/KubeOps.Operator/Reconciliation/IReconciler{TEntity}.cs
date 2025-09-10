// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Controller;

namespace KubeOps.Operator.Reconciliation;

public interface IReconciler<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    Task<Result<TEntity>> ReconcileCreation(TEntity entity, CancellationToken cancellationToken);

    Task<Result<TEntity>> ReconcileModification(TEntity entity, CancellationToken cancellationToken);

    Task<Result<TEntity>> ReconcileDeletion(TEntity entity, CancellationToken cancellationToken);
}
