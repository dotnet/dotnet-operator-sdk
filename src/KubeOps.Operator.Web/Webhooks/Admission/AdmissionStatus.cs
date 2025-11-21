// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Http;

namespace KubeOps.Operator.Web.Webhooks.Admission;

/// <summary>
/// The admission status for the response to the API.
/// </summary>
/// <param name="Message">A message that is passed to the API.</param>
/// <param name="Code">A custom status code to provide more detailed information.</param>
public sealed record AdmissionStatus([property: JsonPropertyName("message")]
    string Message, [property: JsonPropertyName("code")]
    int? Code = StatusCodes.Status200OK);
