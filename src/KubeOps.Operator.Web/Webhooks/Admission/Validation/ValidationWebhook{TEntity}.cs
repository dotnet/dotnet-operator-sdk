// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace KubeOps.Operator.Web.Webhooks.Admission.Validation;

/// <summary>
/// The abstract base for any validation webhook. To use them, attach controllers in the
/// main program and map controllers as well. A validation webhook must be decorated
/// with the <see cref="ValidationWebhookAttribute"/> and the type must be provided.
/// There are async and sync methods for each operation. The async will take
/// precedence if both are implemented (i.e. overridden).
/// </summary>
/// <typeparam name="TEntity">The type of the entity that is validated.</typeparam>
/// <example>
/// Simple example of a webhook that checks nothing but is called on every "CREATE" event.
/// <code>
/// [ValidationWebhook(typeof(V1TestEntity))]
/// public class TestValidationWebhook : ValidationWebhook&lt;V1TestEntity&gt;
/// {
///     public override ValidationResult Create(V1TestEntity entity, bool dryRun)
///     {
///         return Success();
///     }
/// }
/// </code>
/// </example>
[ApiController]
public abstract class ValidationWebhook<TEntity> : ControllerBase
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private const string CreateOperation = "CREATE";
    private const string UpdateOperation = "UPDATE";
    private const string DeleteOperation = "DELETE";

    /// <summary>
    /// Validation callback for entities that are created.
    /// </summary>
    /// <param name="entity">The (soon to be) new entity.</param>
    /// <param name="dryRun">Flag that indicates if the webhook was called in a dry-run.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValidationResult"/>.</returns>
    [NonAction]
    public virtual Task<ValidationResult> CreateAsync(
        TEntity entity,
        bool dryRun,
        CancellationToken cancellationToken) =>
        Task.FromResult(Create(entity, dryRun));

    /// <inheritdoc cref="CreateAsync"/>
    [NonAction]
    public virtual ValidationResult Create(TEntity entity, bool dryRun) => Success();

    /// <summary>
    /// Validation callback for entities that are updated.
    /// </summary>
    /// <param name="oldEntity">The currently stored entity in the Kubernetes API.</param>
    /// <param name="newEntity">The new entity that should be stored.</param>
    /// <param name="dryRun">Flag that indicates if the webhook was called in a dry-run.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValidationResult"/>.</returns>
    [NonAction]
    public virtual Task<ValidationResult> UpdateAsync(
        TEntity oldEntity,
        TEntity newEntity,
        bool dryRun,
        CancellationToken cancellationToken) =>
        Task.FromResult(Update(oldEntity, newEntity, dryRun));

    /// <inheritdoc cref="UpdateAsync"/>
    [NonAction]
    public virtual ValidationResult Update(TEntity oldEntity, TEntity newEntity, bool dryRun) => Success();

    /// <summary>
    /// Validation callback for entities that are to be deleted.
    /// </summary>
    /// <param name="entity">The (soon to be removed) entity.</param>
    /// <param name="dryRun">Flag that indicates if the webhook was called in a dry-run.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValidationResult"/>.</returns>
    [NonAction]
    public virtual Task<ValidationResult>
        DeleteAsync(TEntity entity, bool dryRun, CancellationToken cancellationToken) =>
        Task.FromResult(Delete(entity, dryRun));

    /// <inheritdoc cref="DeleteAsync"/>
    [NonAction]
    public virtual ValidationResult Delete(TEntity entity, bool dryRun) => Success();

    /// <summary>
    /// Public, non-virtual method that is called by the controller.
    /// This method will call the correct method based on the operation.
    /// </summary>
    /// <param name="request">The incoming admission request for an entity.</param>
    /// <param name="cancellationToken">The incoming cancellation token that's cancelled if the request gets aborted.</param>
    /// <returns>The <see cref="ValidationResult"/>.</returns>
    [HttpPost]
    public async Task<IActionResult> Validate(
        [ModelBinder<AdmissionRequestModelBinder>][FromBody] AdmissionRequest<TEntity> request,
        CancellationToken cancellationToken)
    {
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

        return result with { Uid = request.Request.Uid };
    }

    /// <summary>
    /// Create a <see cref="ValidationResult"/> with an optional list of warnings.
    /// The validation will succeed, such that the operation will proceed.
    /// </summary>
    /// <param name="warnings">A list of warnings that is presented to the user.</param>
    /// <returns>A <see cref="ValidationResult"/>.</returns>
    [NonAction]
    protected ValidationResult Success(params string[] warnings)
        => new() { Warnings = warnings };

    /// <summary>
    /// Create a <see cref="ValidationResult"/> that will fail the validation.
    /// The user will only see that the validation failed.
    /// </summary>
    /// <returns>A <see cref="ValidationResult"/>.</returns>
    [NonAction]
    protected ValidationResult Fail()
        => new(false);

    /// <summary>
    /// Create a <see cref="ValidationResult"/> that will fail the validation.
    /// The reason is presented to the user.
    /// </summary>
    /// <param name="reason">A reason for the failure of the validation.</param>
    /// <returns>A <see cref="ValidationResult"/>.</returns>
    [NonAction]
    protected ValidationResult Fail(string reason)
        => Fail() with { Status = new(reason) };

    /// <summary>
    /// Create a <see cref="ValidationResult"/> that will fail the validation.
    /// The reason is presented to the user with the custom status code.
    /// The custom status code may provide further specific information about the
    /// failure, but not all Kubernetes clusters support custom status codes.
    /// </summary>
    /// <param name="reason">A reason for the failure of the validation.</param>
    /// <param name="statusCode">The custom status code.</param>
    /// <returns>A <see cref="ValidationResult"/>.</returns>
    [NonAction]
    protected ValidationResult Fail(string reason, int statusCode) => Fail() with { Status = new(reason, statusCode), };
}
