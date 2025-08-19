// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.Versioning;
using System.Text.Json.Serialization;

namespace KubeOps.Operator.Web.Webhooks.Conversion;

/// <summary>
/// Status object for the conversion. Reports the success / failure of the conversion
/// to the Kubernetes API.
/// </summary>
/// <param name="Message">If set, reports the reason for the failure. Otherwise, the conversion is a success.</param>
[RequiresPreviewFeatures(
    "Conversion webhooks API is not yet stable, the way that conversion " +
    "webhooks are implemented may change in the future based on user feedback.")]
public record ConversionStatus([property: JsonPropertyName("message")]
    string? Message = null)
{
    [JsonPropertyName("status")]
    public string Status => string.IsNullOrWhiteSpace(Message) ? "Success" : "Failed";
}
