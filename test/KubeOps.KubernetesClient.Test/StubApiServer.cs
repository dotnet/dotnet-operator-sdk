// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Text;
using System.Web;

using k8s;

namespace KubeOps.KubernetesClient.Test;

/// <summary>
/// Stands in for the Kubernetes API server: records every request and answers it with <see cref="Response"/>,
/// or with <c>404</c> without one.
/// </summary>
internal sealed class StubApiServer : DelegatingHandler
{
    public List<StubApiRequest> Requests { get; } = [];

    public object? Response { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(new(
            request.Method.Method,
            request.RequestUri!,
            request.Content?.Headers.ContentType?.MediaType,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

        return Response is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(KubernetesJson.Serialize(Response), Encoding.UTF8, "application/json"),
            };
    }
}

internal sealed record StubApiRequest(string Method, Uri Uri, string? ContentType, string? Body)
{
    public string? FieldManager => HttpUtility.ParseQueryString(Uri.Query)["fieldManager"];
}
