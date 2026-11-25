// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.TestHelpers;

namespace KubeOps.KubernetesClient.Test;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class KubernetesClientPatchTest : IDisposable
{
    private static readonly KubernetesClientConfiguration Config = new() { Host = "https://kubernetes.test" };

    private readonly StubApiServer _apiServer = new() { Response = ConfigMap() };
    private readonly IKubernetesClient _client;

    public KubernetesClientPatchTest()
    {
        _client = new KubernetesClient(Config, new Kubernetes(Config, _apiServer));
    }

    [Fact]
    public async Task Patch_From_To_Sends_Only_The_Operations_The_Filter_Keeps()
    {
        await _client.PatchAsync(
            ConfigMap(),
            Changed(),
            operations => operations.Where(o => o.Path.StartsWith("/data")).ToList(),
            cancellationToken: TestContext.Current.CancellationToken);

        _apiServer.Requests.Should().ContainSingle()
            .Which.Body.Should().Be("""[{"op":"replace","path":"/data/key","value":"changed"}]""");
    }

    [Fact]
    public async Task Patch_From_To_Without_Filter_Sends_The_Operations_The_Default_Filter_Keeps()
    {
        var to = Changed();
        to.Metadata.ResourceVersion = "2";

        await _client.PatchAsync(ConfigMap(), to, cancellationToken: TestContext.Current.CancellationToken);

        _apiServer.Requests.Should().ContainSingle().Which.Body.Should().Be(
            """[{"op":"replace","path":"/data/key","value":"changed"},"""
            + """{"op":"add","path":"/metadata/labels","value":{"app":"demo"}}]""");
    }

    [Fact]
    public async Task Patch_From_To_With_A_Filter_Does_Not_Send_The_Operations_The_Default_Filter_Removes()
    {
        var to = Changed();
        to.Metadata.ResourceVersion = "2";

        await _client.PatchAsync(
            ConfigMap(),
            to,
            operations => operations,
            cancellationToken: TestContext.Current.CancellationToken);

        _apiServer.Requests.Should().ContainSingle().Which.Body.Should().NotContain("resourceVersion");
    }

    [Fact]
    public async Task Patch_From_To_Without_Operations_After_The_Filter_Sends_No_Request()
    {
        var from = ConfigMap();

        var result = await _client.PatchAsync(
            from,
            Changed(),
            _ => [],
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(from);
        _apiServer.Requests.Should().BeEmpty();
    }

    public void Dispose() => _client.Dispose();

    private static V1ConfigMap ConfigMap() => new()
    {
        Kind = V1ConfigMap.KubeKind,
        ApiVersion = V1ConfigMap.KubeApiVersion,
        Metadata = new() { Name = "my-config", NamespaceProperty = "default", ResourceVersion = "1" },
        Data = new Dictionary<string, string> { ["key"] = "value" },
    };

    private static V1ConfigMap Changed()
    {
        var configMap = ConfigMap();
        configMap.Data["key"] = "changed";
        configMap.Metadata.Labels = new Dictionary<string, string> { ["app"] = "demo" };
        return configMap;
    }
}
