// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Builder;

namespace KubeOps.KubernetesClient;

/// <summary>
/// Adds <c>fieldManager=&lt;name&gt;</c> to every <c>POST</c>, <c>PUT</c> and <c>PATCH</c> request sent
/// to the Kubernetes API server, so the API server records the written fields under that name in
/// <c>metadata.managedFields</c> instead of deriving a manager from the user agent.
/// </summary>
/// <remarks>
/// <para>
/// A request that already carries a <c>fieldManager</c> query parameter keeps it, so an explicit field manager
/// (e.g. of a server-side apply) always wins. Requests to the connect subresources (<c>proxy</c>, <c>exec</c>,
/// <c>attach</c> and <c>portforward</c>) stay unchanged, because they do not write an object and the API server
/// forwards the query of a proxy request to the backend.
/// </para>
/// <para>
/// Pass the handler to the <see cref="k8s.Kubernetes"/> constructor:
/// <c>new Kubernetes(config, new FieldManagerHandler("my-operator"))</c>.
/// Like every <see cref="DelegatingHandler"/>, an instance must not be shared between clients.
/// </para>
/// </remarks>
public sealed class FieldManagerHandler : DelegatingHandler
{
    private const string FieldManagerParameter = "fieldManager";

    private static readonly HashSet<string> ConnectSubresources =
        new(["proxy", "exec", "attach", "portforward"], StringComparer.Ordinal);

    private readonly string _queryParameter;

    /// <summary>
    /// Creates a handler that writes with the given field manager.
    /// </summary>
    /// <param name="fieldManager">
    /// The field manager: not empty, at most <see cref="FieldManagerName.MaxLength"/> bytes and only
    /// printable characters.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="fieldManager"/> is not a valid field manager.</exception>
    public FieldManagerHandler(string fieldManager)
    {
        FieldManagerName.ThrowIfInvalid(fieldManager);
        FieldManager = fieldManager;
        _queryParameter = $"{FieldManagerParameter}={Uri.EscapeDataString(fieldManager)}";
    }

    /// <summary>
    /// The field manager added to write requests.
    /// </summary>
    public string FieldManager { get; }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method) &&
            request.RequestUri is { IsAbsoluteUri: true } uri &&
            !HasFieldManager(uri.Query) &&
            !IsConnectRequest(uri.AbsolutePath))
        {
            request.RequestUri = new UriBuilder(uri)
            {
                Query = uri.Query.Length > 1 ? $"{uri.Query[1..]}&{_queryParameter}" : _queryParameter,
            }.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch;

    // Query parameter names are case-sensitive for the API server.
    private static bool HasFieldManager(string query)
    {
        var parameters = query.AsSpan().TrimStart('?');
        foreach (var range in parameters.Split('&'))
        {
            var parameter = parameters[range];
            if (parameter.StartsWith(FieldManagerParameter, StringComparison.Ordinal) &&
                (parameter.Length == FieldManagerParameter.Length || parameter[FieldManagerParameter.Length] == '='))
            {
                return true;
            }
        }

        return false;
    }

    // Resource paths are [prefix]/api/{version}/... or [prefix]/apis/{group}/{version}/..., optionally followed
    // by namespaces/{namespace}, then {resource}/{name}/{subresource}.
    private static bool IsConnectRequest(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var root = Array.FindIndex(segments, segment => segment is "api" or "apis");
        if (root < 0)
        {
            return false;
        }

        var start = root + (segments[root] == "api" ? 2 : 3);
        if (segments.Length > start + 2 && segments[start] == "namespaces")
        {
            start += 2;
        }

        return segments.Length > start + 2 && ConnectSubresources.Contains(segments[start + 2]);
    }
}
