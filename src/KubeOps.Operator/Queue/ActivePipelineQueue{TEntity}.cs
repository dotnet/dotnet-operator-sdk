// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Scoped holder for the queue of the controller pipeline that is currently reconciling. The reconciler
/// sets <see cref="Current"/> when it creates the reconciliation scope, so an
/// <c>EntityQueue&lt;TEntity&gt;</c> delegate injected into the controller (or any scoped service)
/// routes requeues back into the pipeline the reconciliation originated from — even when multiple
/// controller pipelines exist for the same entity type.
/// </summary>
/// <typeparam name="TEntity">The Kubernetes entity type.</typeparam>
internal sealed class ActivePipelineQueue<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    /// <summary>
    /// Gets or sets the queue of the pipeline reconciling in the current scope, or <see langword="null"/>
    /// outside a reconciliation scope.
    /// </summary>
    public ITimedEntityQueue<TEntity>? Current { get; set; }
}
