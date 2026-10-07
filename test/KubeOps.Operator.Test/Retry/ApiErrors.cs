// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http.Headers;

using k8s.Autorest;

namespace KubeOps.Operator.Test.Retry;

/// <summary>Builds the exceptions the Kubernetes REST client throws for failed API calls.</summary>
internal static class ApiErrors
{
    public static HttpOperationException Http(HttpStatusCode statusCode, int? retryAfterSeconds = null)
    {
        using var response = new HttpResponseMessage(statusCode);
        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }

        return new HttpOperationException($"Operation returned an invalid status code '{statusCode}'")
        {
            Response = new HttpResponseMessageWrapper(response, string.Empty),
        };
    }
}
