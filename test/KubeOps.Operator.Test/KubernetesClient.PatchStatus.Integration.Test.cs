// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;

using FluentAssertions;

using k8s.Autorest;
using k8s.Models;

using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test;

[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class KubernetesClientPatchStatusIntegrationTest : IntegrationTestBase
{
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task PatchStatus_Succeeds_With_An_Outdated_Entity_Where_UpdateStatus_Conflicts()
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

        var update = () => _client.UpdateStatusAsync(outdated!, cancellationToken);
        await update.Should().ThrowAsync<HttpOperationException>()
            .Where(e => e.Response.StatusCode == HttpStatusCode.Conflict);

        var patched = await _client.PatchStatusAsync(
            outdated!,
            e => e.Status.Status = "patched",
            cancellationToken);

        patched.Status.Status.Should().Be("patched");
        patched.Spec.Username.Should().Be("changed");
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
