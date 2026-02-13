// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace KubeOps.Operator.Web.Webhooks.Admission.Mutation;

/// <summary>
/// The abstract base for any mutation webhook. To use them, attach controllers in the
/// main program and map controllers as well. A mutating webhook must be decorated
/// with the <see cref="MutationWebhookAttribute"/> and the type must be provided.
/// There are async and sync methods for each operation. The async will take
/// precedence if both are implemented (i.e. overridden).
/// </summary>
/// <typeparam name="TEntity">The type of the entity that is mutated.</typeparam>
/// <example>
/// Simple example of a webhook that sets all usernames to "hidden".
/// <code>
/// [MutationWebhook(typeof(V1TestEntity))]
/// public class TestMutationWebhook : MutationWebhook&lt;V1TestEntity&gt;
/// {
///     public override MutationResult&lt;V1TestEntity&gt; Create(V1TestEntity entity, bool dryRun)
///     {
///         entity.Spec.Username = "hidden";
///         return Modified(entity);
///     }
/// }
/// </code>
/// </example>
[ApiController]
public abstract class MutationWebhook<TEntity> : ControllerBase
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private const string CreateOperation = "CREATE";
    private const string UpdateOperation = "UPDATE";
    private const string DeleteOperation = "DELETE";

    /// <summary>
    /// Mutation callback for entities that are created.
    /// </summary>
    /// <param name="entity">The (soon to be) new entity.</param>
    /// <param name="dryRun">Flag that indicates if the webhook was called in a dry-run.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="MutationResult{TEntity}"/>.</returns>
    [NonAction]
    public virtual Task<MutationResult<TEntity>> CreateAsync(
        TEntity entity,
        bool dryRun,
        CancellationToken cancellationToken) =>
        Task.FromResult(Create(entity, dryRun));

    /// <inheritdoc cref="CreateAsync"/>
    [NonAction]
    public virtual MutationResult<TEntity> Create(TEntity entity, bool dryRun) => NoChanges();

    /// <summary>
    /// Mutation callback for entities that are updated.
    /// </summary>
    /// <param name="oldEntity">The currently stored entity in the Kubernetes API.</param>
    /// <param name="newEntity">The new entity that should be stored.</param>
    /// <param name="dryRun">Flag that indicates if the webhook was called in a dry-run.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="MutationResult{TEntity}"/>.</returns>
    [NonAction]
    public virtual Task<MutationResult<TEntity>> UpdateAsync(
        TEntity oldEntity,
        TEntity newEntity,
        bool dryRun,
        CancellationToken cancellationToken) =>
        Task.FromResult(Update(oldEntity, newEntity, dryRun));

    /// <inheritdoc cref="UpdateAsync"/>
    [NonAction]
    public virtual MutationResult<TEntity> Update(TEntity oldEntity, TEntity newEntity, bool dryRun) => NoChanges();

    /// <summary>
    /// Mutation callback for entities that are to be deleted.
    /// </summary>
    /// <param name="entity">The (soon to be removed) entity.</param>
    /// <param name="dryRun">Flag that indicates if the webhook was called in a dry-run.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="MutationResult{TEntity}"/>.</returns>
    [NonAction]
    public virtual Task<MutationResult<TEntity>> DeleteAsync(
        TEntity entity,
        bool dryRun,
        CancellationToken cancellationToken) =>
        Task.FromResult(Delete(entity, dryRun));

    /// <inheritdoc cref="DeleteAsync"/>
    [NonAction]
    public virtual MutationResult<TEntity> Delete(TEntity entity, bool dryRun) => NoChanges();

    /// <summary>
    /// Public, non-virtual method that is called by the controller.
    /// This method will call the correct method based on the operation.
    /// </summary>
    /// <param name="request">The incoming admission request for an entity.</param>
    /// <param name="cancellationToken">The incoming cancellation token that's cancelled if the request gets aborted.</param>
    /// <returns>The <see cref="MutationResult{TEntity}"/>.</returns>
    [HttpPost]
    public async Task<IActionResult> Mutate(
        [ModelBinder<AdmissionRequestModelBinder>][FromBody] AdmissionRequest<TEntity> request,
        CancellationToken cancellationToken)
    {
#pragma warning disable CA2252 // TODO: remove this once the patch is stable.
        var original = request.Request.Operation switch
        {
            CreateOperation or UpdateOperation => request.Request.Object!.ToNode(),
            _ => request.Request.OldObject!.ToNode(),
        };
#pragma warning restore CA2252

        var result = request.Request.Operation switch
        {
            CreateOperation => await CreateAsync(request.Request.Object!, request.Request.DryRun, cancellationToken),
            UpdateOperation => await UpdateAsync(
                request.Request.OldObject!,
                request.Request.Object!,
                request.Request.DryRun,
                cancellationToken),
            DeleteOperation => await DeleteAsync(request.Request.OldObject!, request.Request.DryRun, cancellationToken),
            _ => Fail(
                $"Operation {request.Request.Operation} is not supported.",
                StatusCodes.Status422UnprocessableEntity),
        };

        return result with { Uid = request.Request.Uid, OriginalObject = original };
    }

    /// <summary>
    /// Create a <see cref="MutationResult{TEntity}"/> with an optional list of warnings.
    /// The mutation result indicates that no changes are required for the entity.
    /// </summary>
    /// <param name="warnings">A list of warnings that is presented to the user.</param>
    /// <returns>A <see cref="MutationResult{TEntity}"/>.</returns>
    [NonAction]
    protected MutationResult<TEntity> NoChanges(params string[] warnings)
        => new() { Warnings = warnings };

    /// <summary>
    /// Create a <see cref="MutationResult{TEntity}"/> with an optional list of warnings.
    /// The mutation result indicates that the entity needs to be patched before
    /// it is definitely stored. This creates a JSON Patch between the original object
    /// and the changed entity. If warnings are provided, they are presented to the user.
    /// </summary>
    /// <param name="entity">The modified entity.</param>
    /// <param name="warnings">A list of warnings that is presented to the user.</param>
    /// <returns>A <see cref="MutationResult{TEntity}"/> that indicates changes.</returns>
    [NonAction]
    protected MutationResult<TEntity> Modified(TEntity entity, params string[] warnings)
        => new(entity) { Warnings = warnings };

    /// <summary>
    /// Create a <see cref="MutationResult{TEntity}"/> that will fail the mutation.
    /// The user will only see that the mutation failed.
    /// </summary>
    /// <returns>A <see cref="MutationResult{TEntity}"/>.</returns>
    [NonAction]
    protected MutationResult<TEntity> Fail()
        => new() { Valid = false };

    /// <summary>
    /// Create a <see cref="MutationResult{TEntity}"/> that will fail the mutation.
    /// The reason is presented to the user.
    /// </summary>
    /// <param name="reason">A reason for the failure of the mutation.</param>
    /// <returns>A <see cref="MutationResult{TEntity}"/>.</returns>
    [NonAction]
    protected MutationResult<TEntity> Fail(string reason)
        => Fail() with { Status = new(reason) };

    /// <summary>
    /// Create a <see cref="MutationResult{TEntity}"/> that will fail the mutation.
    /// The reason is presented to the user with the custom status code.
    /// The custom status code may provide further specific information about the
    /// failure, but not all Kubernetes clusters support custom status codes.
    /// </summary>
    /// <param name="reason">A reason for the failure of the mutation.</param>
    /// <param name="statusCode">The custom status code.</param>
    /// <returns>A <see cref="MutationResult{TEntity}"/>.</returns>
    [NonAction]
    protected MutationResult<TEntity> Fail(string reason, int statusCode) =>
        Fail() with { Status = new(reason, statusCode), };
}
