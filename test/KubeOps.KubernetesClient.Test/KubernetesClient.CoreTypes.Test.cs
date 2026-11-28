// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Web;

using FluentAssertions;

using k8s;
using k8s.Autorest;
using k8s.Models;

namespace KubeOps.KubernetesClient.Test;

/// <summary>
/// The client sends requests for every type through the custom objects API. For the core group (empty group),
/// the Kubernetes client turns its <c>/apis//v1</c> path into <c>/api/v1</c>; these tests pin the paths that are sent.
/// </summary>
[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class KubernetesClientCoreTypesTest : IDisposable
{
    private static readonly KubernetesClientConfiguration Config = new() { Host = "https://kubernetes.test" };

    private readonly StubApiServer _apiServer = new() { Response = ConfigMap() };
    private readonly IKubernetesClient _client;

    public KubernetesClientCoreTypesTest()
    {
        _client = new KubernetesClient(Config, new Kubernetes(Config, _apiServer));
    }

    [Fact]
    public async Task Get_Uses_The_Core_Api_Path()
    {
        await _client.GetAsync<V1ConfigMap>("my-config", "default", TestContext.Current.CancellationToken);

        SentRequest().Should().Be("GET /api/v1/namespaces/default/configmaps/my-config");
    }

    [Fact]
    public async Task List_Uses_The_Core_Api_Path()
    {
        _apiServer.Response = new V1ConfigMapList { Items = [] };

        await _client.ListAsync<V1ConfigMap>("default", cancellationToken: TestContext.Current.CancellationToken);

        SentRequest().Should().Be("GET /api/v1/namespaces/default/configmaps");
    }

    [Fact]
    public async Task Get_Of_A_Cluster_Scoped_Core_Type_Uses_The_Core_Api_Path()
    {
        await _client.GetAsync<V1Namespace>("my-namespace", cancellationToken: TestContext.Current.CancellationToken);

        SentRequest().Should().Be("GET /api/v1/namespaces/my-namespace");
    }

    [Fact]
    public async Task List_Of_All_Namespaces_Uses_The_Core_Api_Path()
    {
        _apiServer.Response = new V1ConfigMapList { Items = [] };

        await _client.ListAsync<V1ConfigMap>(cancellationToken: TestContext.Current.CancellationToken);

        SentRequest().Should().Be("GET /api/v1/configmaps");
    }

    [Theory]
    [InlineData("default", "/api/v1/namespaces/default/configmaps")]
    [InlineData(null, "/api/v1/configmaps")]
    public async Task Watch_Uses_The_Core_Api_Path(string? @namespace, string path)
    {
        // Without a response, the stub answers 404 and the watch fails after sending its request.
        _apiServer.Response = null;

        var watch = async () =>
        {
            await foreach (var _ in _client.WatchAsync<V1ConfigMap>(
                               @namespace,
                               cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        };

        await watch.Should().ThrowAsync<HttpOperationException>();
        SentRequest().Should().Be($"GET {path}");
        HttpUtility.ParseQueryString(_apiServer.Requests[0].Uri.Query)["watch"].Should().Be("true");
    }

    [Fact]
    public async Task Create_Uses_The_Core_Api_Path()
    {
        await _client.CreateAsync(ConfigMap(), TestContext.Current.CancellationToken);

        SentRequest().Should().Be("POST /api/v1/namespaces/default/configmaps");
    }

    [Fact]
    public async Task Update_Uses_The_Core_Api_Path()
    {
        await _client.UpdateAsync(ConfigMap(), TestContext.Current.CancellationToken);

        SentRequest().Should().Be("PUT /api/v1/namespaces/default/configmaps/my-config");
    }

    [Fact]
    public async Task Patch_Uses_The_Core_Api_Path()
    {
        var to = ConfigMap();
        to.Data = new Dictionary<string, string> { ["key"] = "value" };

        await _client.PatchAsync(ConfigMap(), to, cancellationToken: TestContext.Current.CancellationToken);

        SentRequest().Should().Be("PATCH /api/v1/namespaces/default/configmaps/my-config");
    }

    [Fact]
    public async Task Delete_Uses_The_Core_Api_Path()
    {
        await _client.DeleteAsync(ConfigMap(), TestContext.Current.CancellationToken);

        SentRequest().Should().Be("DELETE /api/v1/namespaces/default/configmaps/my-config");
    }

    [Fact]
    public async Task UpdateStatus_Of_A_Namespaced_Core_Type_Uses_The_Core_Api_Path()
    {
        var pod = new V1Pod
        {
            ApiVersion = V1Pod.KubeApiVersion,
            Kind = V1Pod.KubeKind,
            Metadata = new() { Name = "my-pod", NamespaceProperty = "default" },
            Status = new() { Phase = "Running" },
        };

        _apiServer.Response = pod;

        await _client.UpdateStatusAsync(pod, TestContext.Current.CancellationToken);

        SentRequest().Should().Be("PATCH /api/v1/namespaces/default/pods/my-pod/status");
    }

    [Fact]
    public async Task UpdateStatus_Of_A_Cluster_Scoped_Core_Type_Uses_The_Core_Api_Path()
    {
        var @namespace = new V1Namespace
        {
            ApiVersion = V1Namespace.KubeApiVersion,
            Kind = V1Namespace.KubeKind,
            Metadata = new() { Name = "my-namespace" },
            Status = new() { Phase = "Active" },
        };

        _apiServer.Response = @namespace;

        await _client.UpdateStatusAsync(@namespace, TestContext.Current.CancellationToken);

        SentRequest().Should().Be("PATCH /api/v1/namespaces/my-namespace/status");
    }

    public void Dispose() => _client.Dispose();

    private static V1ConfigMap ConfigMap()
        => new()
        {
            ApiVersion = V1ConfigMap.KubeApiVersion,
            Kind = V1ConfigMap.KubeKind,
            Metadata = new() { Name = "my-config", NamespaceProperty = "default" },
        };

    private string SentRequest()
    {
        var request = _apiServer.Requests.Should().ContainSingle().Subject;
        return $"{request.Method} {request.Uri.AbsolutePath}";
    }
}
