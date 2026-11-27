// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.TestHelpers;

namespace KubeOps.KubernetesClient.Test;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class KubernetesClientUpdateStatusTest : IDisposable
{
    private static readonly KubernetesClientConfiguration Config = new() { Host = "https://kubernetes.test" };

    private readonly StubApiServer _apiServer = new() { Response = new V1StatusTestEntity() };
    private readonly IKubernetesClient _client;

    public KubernetesClientUpdateStatusTest()
    {
        _client = new KubernetesClient(Config, new Kubernetes(Config, _apiServer));
    }

    [Fact]
    public async Task UpdateStatus_Applies_Only_The_Status_With_Force_To_The_Status_Subresource()
    {
        var entity = new V1StatusTestEntity
        {
            Metadata = new() { Name = "my-entity", NamespaceProperty = "default", ResourceVersion = "1" },
            Spec = new() { Replicas = 3 },
            Status = new() { Phase = "Ready" },
        };

        await _client.UpdateStatusAsync(entity, TestContext.Current.CancellationToken);

        var request = _apiServer.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be("PATCH");
        request.Uri.PathAndQuery.Should()
            .Be("/apis/client.test/v1/namespaces/default/statustests/my-entity/status?force=true");
        request.ContentType.Should().Be("application/apply-patch+yaml");
        JsonNode.DeepEquals(JsonNode.Parse(request.Body!), JsonNode.Parse("""
            {
              "apiVersion": "client.test/v1",
              "kind": "StatusTest",
              "metadata": { "name": "my-entity", "namespace": "default" },
              "status": { "phase": "Ready" }
            }
            """)).Should().BeTrue(request.Body);
    }

    [Fact]
    public async Task UpdateStatus_Of_A_Cluster_Scoped_Entity_Sends_No_Namespace()
    {
        var entity = new V1ClusterStatusTestEntity
        {
            Metadata = new() { Name = "my-entity" },
            Status = new() { Phase = "Ready" },
        };

        await _client.UpdateStatusAsync(entity, TestContext.Current.CancellationToken);

        var request = _apiServer.Requests.Should().ContainSingle().Subject;
        request.Uri.PathAndQuery.Should().Be("/apis/client.test/v1/clusterstatustests/my-entity/status?force=true");
        JsonNode.DeepEquals(JsonNode.Parse(request.Body!), JsonNode.Parse("""
            {
              "apiVersion": "client.test/v1",
              "kind": "ClusterStatusTest",
              "metadata": { "name": "my-entity" },
              "status": { "phase": "Ready" }
            }
            """)).Should().BeTrue(request.Body);
    }

    public void Dispose() => _client.Dispose();

    [KubernetesEntity(Group = "client.test", ApiVersion = "v1", Kind = "StatusTest", PluralName = "statustests")]
    private sealed class V1StatusTestEntity
        : CustomKubernetesEntity<V1StatusTestEntity.EntitySpec, V1StatusTestEntity.EntityStatus>
    {
        public sealed class EntitySpec
        {
            public int Replicas { get; set; }
        }

        public sealed class EntityStatus
        {
            public string? Phase { get; set; }
        }
    }

    [KubernetesEntity(
        Group = "client.test",
        ApiVersion = "v1",
        Kind = "ClusterStatusTest",
        PluralName = "clusterstatustests")]
    private sealed class V1ClusterStatusTestEntity
        : CustomKubernetesEntity<V1StatusTestEntity.EntitySpec, V1StatusTestEntity.EntityStatus>;
}
