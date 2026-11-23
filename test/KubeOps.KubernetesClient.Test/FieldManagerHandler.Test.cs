// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;

using FluentAssertions;

namespace KubeOps.KubernetesClient.Test;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class FieldManagerHandlerTest
{
    private const string BaseUri = "https://kubernetes.test/api/v1/namespaces/default/configmaps";

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("patch")]
    public async Task Adds_The_Field_Manager_To_Write_Requests(string method)
    {
        var uri = await SendAsync(new HttpMethod(method), BaseUri);

        uri.Should().Be($"{BaseUri}?fieldManager=my-operator");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Leaves_Other_Requests_Unchanged(string method)
    {
        var uri = await SendAsync(new HttpMethod(method), BaseUri);

        uri.Should().Be(BaseUri);
    }

    [Fact]
    public async Task Keeps_Existing_Query_Parameters()
    {
        var uri = await SendAsync(HttpMethod.Post, $"{BaseUri}?dryRun=All&fieldValidation=Strict");

        uri.Should().Be($"{BaseUri}?dryRun=All&fieldValidation=Strict&fieldManager=my-operator");
    }

    [Theory]
    [InlineData("?fieldManager=server-side-apply&force=true")]
    [InlineData("?force=true&fieldManager=server-side-apply")]
    [InlineData("?fieldManager=")]
    public async Task Keeps_An_Explicit_Field_Manager(string query)
    {
        var uri = await SendAsync(HttpMethod.Patch, $"{BaseUri}{query}");

        uri.Should().Be($"{BaseUri}{query}");
    }

    [Fact]
    public async Task Does_Not_Treat_Similar_Parameters_As_Field_Manager()
    {
        var uri = await SendAsync(HttpMethod.Post, $"{BaseUri}?labelSelector=fieldManager%3Dx&xfieldManager=y");

        uri.Should().Be($"{BaseUri}?labelSelector=fieldManager%3Dx&xfieldManager=y&fieldManager=my-operator");
    }

    [Theory]
    [InlineData("?FieldManager=x")]
    [InlineData("?fieldmanager=x")]
    public async Task Treats_The_Parameter_Name_Case_Sensitively(string query)
    {
        var uri = await SendAsync(HttpMethod.Post, $"{BaseUri}{query}");

        uri.Should().Be($"{BaseUri}{query}&fieldManager=my-operator");
    }

    [Theory]
    [InlineData("POST", "/api/v1/namespaces/default/pods/my-pod/proxy")]
    [InlineData("PUT", "/api/v1/namespaces/default/services/my-service:http/proxy/hook")]
    [InlineData("PATCH", "/api/v1/nodes/my-node/proxy/configz")]
    [InlineData("POST", "/api/v1/namespaces/default/pods/my-pod/exec")]
    [InlineData("POST", "/api/v1/namespaces/default/pods/my-pod/attach")]
    [InlineData("POST", "/api/v1/namespaces/default/pods/my-pod/portforward")]
    [InlineData("POST", "/k8s/clusters/c-1/api/v1/namespaces/default/pods/my-pod/proxy")]
    public async Task Leaves_Connect_Requests_Unchanged(string method, string path)
    {
        var uri = await SendAsync(new HttpMethod(method), $"https://kubernetes.test{path}?query=value");

        uri.Should().Be($"https://kubernetes.test{path}?query=value");
    }

    [Theory]
    [InlineData("POST", "/api/v1/namespaces/default/pods/my-pod/eviction")]
    [InlineData("PUT", "/api/v1/namespaces/default/configmaps/proxy")]
    [InlineData("PUT", "/api/v1/namespaces/proxy/configmaps/exec")]
    [InlineData("PUT", "/api/v1/namespaces/proxy/status")]
    [InlineData("POST", "/api/v1/namespaces/api/configmaps")]
    [InlineData("PUT", "/apis/demo.kubeops.dev/v1/namespaces/default/proxies/proxy/status")]
    [InlineData("PUT", "/apis/demo.kubeops.dev/v1/proxies/exec/status")]
    [InlineData("POST", "/k8s/clusters/c-1/api/v1/namespaces/default/configmaps")]
    public async Task Adds_The_Field_Manager_To_Other_Subresources_And_Resources(string method, string path)
    {
        var uri = await SendAsync(new HttpMethod(method), $"https://kubernetes.test{path}");

        uri.Should().Be($"https://kubernetes.test{path}?fieldManager=my-operator");
    }

    [Fact]
    public async Task Escapes_The_Field_Manager()
    {
        var uri = await SendAsync(HttpMethod.Post, BaseUri, "my operator&co/ö");

        uri.Should().Be($"{BaseUri}?fieldManager=my%20operator%26co%2F%C3%B6");
    }

    [Fact]
    public void Exposes_The_Field_Manager()
    {
        using var handler = new FieldManagerHandler("my-operator");

        handler.FieldManager.Should().Be("my-operator");
    }

    [Fact]
    public void Rejects_An_Invalid_Field_Manager()
    {
        var act = () => new FieldManagerHandler("my\noperator");

        act.Should().Throw<ArgumentException>().WithParameterName("fieldManager");
    }

    private static async Task<string> SendAsync(HttpMethod method, string uri, string fieldManager = "my-operator")
    {
        var recorder = new RecordingHandler();
        using var invoker = new HttpMessageInvoker(new FieldManagerHandler(fieldManager) { InnerHandler = recorder });
        using var request = new HttpRequestMessage(method, uri);

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        return recorder.RequestUri!.AbsoluteUri;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
