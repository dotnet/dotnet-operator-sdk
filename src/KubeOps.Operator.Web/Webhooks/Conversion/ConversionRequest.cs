// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel.DataAnnotations;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KubeOps.Operator.Web.Webhooks.Conversion;

/// <summary>
/// Incoming conversion request for a webhook.
/// </summary>
[RequiresPreviewFeatures(
    "Conversion webhooks API is not yet stable, the way that conversion " +
    "webhooks are implemented may change in the future based on user feedback.")]
public sealed class ConversionRequest : ConversionReview
{
    /// <summary>
    /// Conversion request data.
    /// </summary>
    [JsonPropertyName("request")]
    [Required]
    public ConversionRequestData Request { get; init; } = new();

    /// <summary>
    /// Data object for the incoming conversion request.
    /// </summary>
    public sealed class ConversionRequestData
    {
        /// <summary>
        /// The unique ID of the conversion request.
        /// </summary>
        [JsonPropertyName("uid")]
        [Required]
        public string Uid { get; init; } = string.Empty;

        /// <summary>
        /// Target group/api version of the conversion.
        /// </summary>
        [JsonPropertyName("desiredAPIVersion")]
        [Required]
        public string DesiredApiVersion { get; init; } = string.Empty;

        /// <summary>
        /// List of objects that need to be converted.
        /// </summary>
        [JsonPropertyName("objects")]
        public JsonNode[] Objects { get; init; } = [];
    }
}
