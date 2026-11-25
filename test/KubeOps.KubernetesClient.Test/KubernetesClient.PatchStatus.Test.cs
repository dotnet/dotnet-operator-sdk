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
public sealed class KubernetesClientPatchStatusTest : IDisposable
{
    private const string StatusPath = "/apis/client.test/v1/namespaces/default/statustests/my-entity/status";

    private static readonly KubernetesClientConfiguration Config = new() { Host = "https://kubernetes.test" };

    private readonly StubApiServer _apiServer = new() { Response = Entity() };
    private readonly IKubernetesClient _client;

    public KubernetesClientPatchStatusTest()
    {
        _client = new KubernetesClient(Config, new Kubernetes(Config, _apiServer));
    }

    [Fact]
    public async Task PatchStatus_With_A_Change_Sends_A_Merge_Patch_Of_The_Changed_Fields_To_The_Status()
    {
        var entity = Entity();

        await _client.PatchStatusAsync(
            entity,
            e => e.Status.Phase = "Ready",
            TestContext.Current.CancellationToken);

        entity.Status.Phase.Should().Be("Ready");
        _apiServer.Requests.Should().ContainSingle().Which.Should().Match<StubApiRequest>(r =>
            r.Method == "PATCH"
            && r.Uri.PathAndQuery == StatusPath
            && r.ContentType == "application/merge-patch+json"
            && r.Body == """{"status":{"phase":"Ready"}}""");
    }

    [Fact]
    public async Task PatchStatus_From_To_Sends_A_Merge_Patch_Of_The_Changed_Fields_To_The_Status()
    {
        var to = Entity();
        to.Status.Phase = "Ready";

        await _client.PatchStatusAsync(Entity(), to, TestContext.Current.CancellationToken);

        _apiServer.Requests.Should().ContainSingle().Which.Body.Should().Be("""{"status":{"phase":"Ready"}}""");
    }

    [Fact]
    public async Task PatchStatus_Without_Changes_Returns_The_Entity_Without_A_Request()
    {
        var entity = Entity();

        var changed = await _client.PatchStatusAsync(entity, _ => { }, TestContext.Current.CancellationToken);
        var fromTo = await _client.PatchStatusAsync(entity, Entity(), TestContext.Current.CancellationToken);

        changed.Should().BeSameAs(entity);
        fromTo.Should().BeSameAs(entity);
        _apiServer.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PatchStatus_With_A_Patch_Of_A_Cluster_Scoped_Entity_Uses_The_Cluster_Path()
    {
        await _client.PatchStatusAsync<V1StatusTestEntity>(
            new V1Patch("""{"status":{"phase":"Ready"}}""", V1Patch.PatchType.ApplyPatch),
            "my-entity",
            fieldManager: "my-operator",
            force: true,
            cancellationToken: TestContext.Current.CancellationToken);

        _apiServer.Requests.Should().ContainSingle().Which.Should().Match<StubApiRequest>(r =>
            r.Uri.PathAndQuery == "/apis/client.test/v1/statustests/my-entity/status?fieldManager=my-operator&force=true"
            && r.ContentType == "application/apply-patch+yaml");
    }

    public void Dispose() => _client.Dispose();

    private static V1StatusTestEntity Entity() => new()
    {
        Metadata = new() { Name = "my-entity", NamespaceProperty = "default", ResourceVersion = "1" },
        Status = new() { Phase = "Pending" },
    };
}
