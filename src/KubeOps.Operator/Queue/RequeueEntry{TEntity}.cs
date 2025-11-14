// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Reconciliation.Queue;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Represents an entry in a requeue system for managing entities of type <typeparamref name="TEntity"/>.
/// The requeue system facilitates the categorization and reprocessing of entities based on their
/// lifecycle events, such as added, modified, or deleted.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the entity associated with the requeue entry.
/// </typeparam>
public readonly record struct RequeueEntry<TEntity>(TEntity Entity, RequeueType RequeueType);
