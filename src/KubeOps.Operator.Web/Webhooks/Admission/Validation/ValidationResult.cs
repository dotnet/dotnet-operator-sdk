// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace KubeOps.Operator.Web.Webhooks.Admission.Validation;

/// <summary>
/// The validation result for the validation (admission) request to a webhook.
/// </summary>
/// <param name="Valid">Whether the validation / the entity is valid or not.</param>
public record ValidationResult(bool Valid = true) : IActionResult
{
    /// <summary>
    /// Provides additional information to the validation result.
    /// The message is displayed to the user if the validation fails.
    /// The status code can provide more information about the error.
    /// <a href="https://kubernetes.io/docs/reference/access-authn-authz/extensible-admission-controllers/#response">
    /// See "Extensible Admission Controller Response"
    /// </a>
    /// </summary>
    public AdmissionStatus? Status { get; init; }

    /// <summary>
    /// Despite being "valid", the validation can add a list of warnings to the user.
    /// If this is not yet supported by the cluster, the field is ignored.
    /// Warnings may contain up to 256 characters but they should be limited to 120 characters.
    /// If more than 4096 characters are submitted, additional messages are ignored.
    /// </summary>
    public IList<string> Warnings { get; init; } = new List<string>();

    internal string Uid { get; init; } = string.Empty;

    /// <inheritdoc/>
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        if (string.IsNullOrWhiteSpace(Uid))
        {
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.WriteAsync("No request UID was provided.");
            return;
        }

        await response.WriteAsJsonAsync(
            new AdmissionResponse
            {
                Response = new()
                {
                    Uid = Uid,
                    Allowed = Valid,
                    Status = Status,
                    Warnings = Warnings.ToArray(),
                },
            });
    }
}
