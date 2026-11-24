// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

namespace KubeOps.KubernetesClient.Test;

[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class KubernetesClientFieldManagerIntegrationTest : IntegrationTestBase, IAsyncDisposable
{
    private const string FieldManager = "kubeops-field-manager-test";

    private readonly IKubernetesClient _client =
        new KubernetesClient(KubernetesClientConfiguration.BuildDefaultConfig(), FieldManager);

    private readonly List<V1ConfigMap> _objects = [];

    [Fact]
    public async Task Create_And_Update_Are_Recorded_Under_The_Field_Manager()
    {
        var config = await CreateConfigMapAsync();
        config.Metadata.ManagedFields.Should().ContainSingle()
            .Which.Should().Match<V1ManagedFieldsEntry>(f => f.Manager == FieldManager && f.Operation == "Update");

        config.Data["updated"] = "value";
        var updated = await _client.UpdateAsync(config, TestContext.Current.CancellationToken);

        updated.Metadata.ManagedFields.Should().OnlyContain(f => f.Manager == FieldManager);
    }

    [Fact]
    public async Task Patch_With_Explicit_Field_Manager_Is_Recorded_Under_That_Manager()
    {
        var config = await CreateConfigMapAsync();

        var patched = await _client.PatchAsync(
            config,
            new V1Patch("""{"data":{"patched":"value"}}""", V1Patch.PatchType.MergePatch),
            fieldManager: "explicit-manager",
            cancellationToken: TestContext.Current.CancellationToken);

        patched.Metadata.ManagedFields.Select(f => f.Manager)
            .Should().BeEquivalentTo(FieldManager, "explicit-manager");
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DeleteAsync(_objects);
        _client.Dispose();
    }

    private async Task<V1ConfigMap> CreateConfigMapAsync()
    {
        var config = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new() { Name = "cm-" + Guid.NewGuid().ToString().ToLower(), NamespaceProperty = "default" },
                Data = new Dictionary<string, string> { { "initial", "value" } },
            },
            TestContext.Current.CancellationToken);

        _objects.Add(config);
        return config;
    }
}
