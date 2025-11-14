// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Reconciliation;

/// <summary>
/// Represents the result of an operation performed on an entity
/// within the context of Kubernetes controllers or finalizers.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes entity associated with this result.
/// Must implement <see cref="IKubernetesObject{TMetadata}"/> where TMetadata is <see cref="V1ObjectMeta"/>.
/// </typeparam>
public sealed record ReconciliationResult<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private ReconciliationResult(TEntity entity, bool isSuccess, string? errorMessage, Exception? error, TimeSpan? requeueAfter)
    {
        Entity = entity;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Error = error;
        RequeueAfter = requeueAfter;
    }

    /// <summary>
    /// Represents the Kubernetes entity associated with the result of an operation or reconciliation process.
    /// This property contains the entity object of type <typeparamref name="TEntity"/> that was processed, modified, or finalized
    /// during an operation. It provides access to the updated state or metadata of the entity after the operation.
    /// Typically used for handling further processing, queuing, or logging of the affected entity.
    /// </summary>
    public TEntity Entity { get; }

    /// <summary>
    /// Indicates whether the operation has completed successfully.
    /// Returns true when the operation was successful, and no errors occurred.
    /// This property is used to determine the state of the operation
    /// and is often checked to decide whether further processing or error handling is necessary.
    /// When this property is true, <see cref="ErrorMessage"/> will be null, as there were no errors.
    /// </summary>
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess { get; }

    /// <summary>
    /// Contains a descriptive message associated with a failure when the operation does not succeed.
    /// Used to provide context or details about the failure, assisting in debugging and logging.
    /// This property is typically set when <see cref="IsSuccess"/> is false.
    /// It will be null for successful operations.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Represents an exception associated with the operation outcome.
    /// If the operation fails, this property may hold the exception that caused the failure,
    /// providing additional context about the error for logging or debugging purposes.
    /// This is optional and may be null if no exception information is available or applicable.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// Specifies the duration to wait before requeuing the entity for reprocessing.
    /// If set, the entity will be scheduled for reprocessing after the specified time span.
    /// This can be useful in scenarios where the entity needs to be revisited later due to external conditions,
    /// such as resource dependencies or transient errors.
    /// </summary>
    public TimeSpan? RequeueAfter { get; set; }

    /// <summary>
    /// Creates a successful result for the given entity, optionally specifying a requeue duration.
    /// </summary>
    /// <param name="entity">
    /// The Kubernetes entity that the result is associated with.
    /// </param>
    /// <param name="requeueAfter">
    /// An optional duration after which the entity should be requeued for processing. Defaults to null.
    /// </param>
    /// <returns>
    /// A successful <see cref="ReconciliationResult{TEntity}"/> instance containing the provided entity and requeue duration.
    /// </returns>
    public static ReconciliationResult<TEntity> Success(TEntity entity, TimeSpan? requeueAfter = null)
        => new(entity, true, null, null, requeueAfter);

    /// <summary>
    /// Creates a failure result for the given entity, specifying an error message and optionally an exception and requeue duration.
    /// </summary>
    /// <param name="entity">
    /// The Kubernetes entity that the result is associated with.
    /// </param>
    /// <param name="errorMessage">
    /// A detailed message describing the reason for the failure.
    /// </param>
    /// <param name="error">
    /// An optional exception that caused the failure. Defaults to null.
    /// </param>
    /// <param name="requeueAfter">
    /// An optional duration after which the entity should be requeued for processing. Defaults to null.
    /// </param>
    /// <returns>
    /// A failure <see cref="ReconciliationResult{TEntity}"/> instance containing the provided entity, error information, and requeue duration.
    /// </returns>
    public static ReconciliationResult<TEntity> Failure(
        TEntity entity, string errorMessage, Exception? error = null, TimeSpan? requeueAfter = null)
        => new(entity, false, errorMessage, error, requeueAfter);
}
