// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test;

[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class KubernetesClientUpdateStatusIntegrationTest : IntegrationTestBase
{
    private readonly IKubernetesClient _client =
        new KubernetesClient.KubernetesClient(KubernetesClientConfiguration.BuildDefaultConfig(), "kubeops-test");

    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task UpdateStatus_Of_An_Outdated_Entity_Applies_The_Status_And_Keeps_The_Spec()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entity = await _client.CreateAsync(
            new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace),
            cancellationToken);
        var outdated = await _client.GetAsync<V1OperatorIntegrationTestEntity>(
            entity.Name(),
            entity.Namespace(),
            cancellationToken);

        entity.Spec.Username = "changed";
        await _client.UpdateAsync(entity, cancellationToken);

        outdated!.Status.Status = "applied";
        var updated = await _client.UpdateStatusAsync(outdated, cancellationToken);

        updated.Status.Status.Should().Be("applied");
        updated.Spec.Username.Should().Be("changed");
        updated.Metadata.ManagedFields.Should().Contain(f =>
            f.Manager == "kubeops-test" && f.Operation == "Apply" && f.Subresource == "status");
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await _ns.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _ns.DisposeAsync();
        _client.Dispose();
    }

    protected override void ConfigureHost(HostApplicationBuilder builder)
    {
    }
}
