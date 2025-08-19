// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace KubeOps.Operator.Web.Webhooks.Admission;

/// <summary>
/// Base class for admission review requests.
/// </summary>
public abstract class AdmissionReview
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion => "admission.k8s.io/v1";

    [JsonPropertyName("kind")]
    public string Kind => "AdmissionReview";
}
