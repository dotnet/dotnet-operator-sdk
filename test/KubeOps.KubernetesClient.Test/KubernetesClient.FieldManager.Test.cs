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
public sealed class KubernetesClientFieldManagerTest : IDisposable
{
    private static readonly KubernetesClientConfiguration Config = new() { Host = "https://kubernetes.test" };

    private readonly StubApiServer _apiServer = new() { Response = ConfigMap() };
    private readonly IKubernetesClient _client;

    public KubernetesClientFieldManagerTest()
    {
        _client = new KubernetesClient(
            Config,
            new Kubernetes(Config, new FieldManagerHandler("my-operator"), _apiServer));
    }

    [Fact]
    public async Task Create_Update_UpdateStatus_Patch_And_PatchStatus_Send_The_Field_Manager()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var patch = new V1Patch("""{"data":{"key":"value"}}""", V1Patch.PatchType.MergePatch);

        await _client.CreateAsync(ConfigMap(), cancellationToken);
        await _client.UpdateAsync(ConfigMap(), cancellationToken);
        await _client.UpdateStatusAsync(ConfigMap(), cancellationToken);
        await _client.PatchAsync(ConfigMap(), patch, cancellationToken: cancellationToken);
        await _client.PatchStatusAsync<V1ConfigMap>(
            patch,
            "my-config",
            "default",
            cancellationToken: cancellationToken);

        _apiServer.Requests.Select(r => (r.Method, r.FieldManager)).Should().Equal(
            ("POST", "my-operator"),
            ("PUT", "my-operator"),
            ("PUT", "my-operator"),
            ("PATCH", "my-operator"),
            ("PATCH", "my-operator"));
    }

    [Fact]
    public async Task Patch_With_Explicit_Field_Manager_Keeps_It()
    {
        await _client.PatchAsync(
            ConfigMap(),
            new V1Patch("""{"data":{"key":"value"}}""", V1Patch.PatchType.ApplyPatch),
            fieldManager: "server-side-apply",
            force: true,
            cancellationToken: TestContext.Current.CancellationToken);

        _apiServer.Requests.Select(r => (r.Method, r.FieldManager)).Should().ContainSingle()
            .Which.Should().Be(("PATCH", "server-side-apply"));
    }

    [Fact]
    public async Task Reads_And_Deletes_Do_Not_Send_A_Field_Manager()
    {
        await _client.GetAsync<V1ConfigMap>("my-config", "default", TestContext.Current.CancellationToken);
        await _client.DeleteAsync(ConfigMap(), TestContext.Current.CancellationToken);

        _apiServer.Requests.Select(r => (r.Method, r.FieldManager))
            .Should().Equal(("GET", null), ("DELETE", null));
    }

    [Fact]
    public void Constructor_With_Field_Manager_Writes_Through_A_Field_Manager_Handler()
    {
        using var client = new KubernetesClient(Config, "my-cli");

        KubernetesHandlerChain.FirstHandlerOf(client.ApiClient).Should().BeOfType<FieldManagerHandler>()
            .Which.FieldManager.Should().Be("my-cli");
    }

    [Fact]
    public void Constructor_With_Field_Manager_Rejects_An_Invalid_Field_Manager()
    {
        var act = () => new KubernetesClient(Config, "my\ncli");

        act.Should().Throw<ArgumentException>().WithParameterName("fieldManager");
    }

    public void Dispose() => _client.Dispose();

    private static V1ConfigMap ConfigMap() => new()
    {
        Kind = V1ConfigMap.KubeKind,
        ApiVersion = V1ConfigMap.KubeApiVersion,
        Metadata = new() { Name = "my-config", NamespaceProperty = "default" },
    };
}
