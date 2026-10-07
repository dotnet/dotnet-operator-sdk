// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Net;

using k8s;
using k8s.Autorest;

namespace KubeOps.Operator.Retry;

/// <summary>
/// Classifies errors of Kubernetes API calls. REST calls (get, list, create, ...) throw
/// <see cref="HttpOperationException"/>, watch streams throw <see cref="KubernetesException"/>.
/// </summary>
internal static class KubernetesApiErrors
{
    public static bool IsTransient(Exception exception) => exception switch
    {
        HttpRequestException or TimeoutException or TaskCanceledException => true,
        KubernetesException { Status.Code: null } => true,
        _ => GetStatusCode(exception) is { } code && IsTransient(code),
    };

    /// <summary>
    /// Returns the server-requested minimum delay (<c>Retry-After</c>, e.g. on <c>429 TooManyRequests</c>), if any.
    /// </summary>
    /// <param name="exception">The failed API call's exception.</param>
    /// <returns>The minimum delay, or <c>null</c> when the server did not request one.</returns>
    public static TimeSpan? GetRetryAfter(Exception exception)
    {
        var seconds = exception switch
        {
            KubernetesException e => e.Status?.Details?.RetryAfterSeconds,
            HttpOperationException { Response.Headers: { } headers } => ParseRetryAfterHeader(headers),
            _ => null,
        };

        return seconds > 0 ? TimeSpan.FromSeconds(seconds.Value) : null;
    }

    /// <summary>
    /// A low-cardinality error classification following the OpenTelemetry <c>error.type</c> convention:
    /// the HTTP status code when the API server answered, otherwise the exception's full type name.
    /// </summary>
    /// <param name="exception">The failed API call's exception.</param>
    /// <returns>The error classification.</returns>
    public static string GetErrorType(Exception exception)
        => GetStatusCode(exception)?.ToString(CultureInfo.InvariantCulture)
           ?? exception.GetType().FullName!;

    private static int? GetStatusCode(Exception exception) => exception switch
    {
        HttpOperationException { Response: { } response } => (int)response.StatusCode,
        KubernetesException e => e.Status?.Code,
        _ => null,
    };

    private static int? ParseRetryAfterHeader(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        foreach (var (name, values) in headers)
        {
            if (!string.Equals(name, "Retry-After", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in values)
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                {
                    return seconds;
                }
            }
        }

        return null;
    }

    private static bool IsTransient(int statusCode) => statusCode switch
    {
        (int)HttpStatusCode.RequestTimeout => true,
        (int)HttpStatusCode.Conflict => true,
        (int)HttpStatusCode.TooManyRequests => true,
        >= 500 => true,
        _ => false,
    };
}
