// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Autorest;

using KubeOps.KubernetesClient;
using KubeOps.Operator.Web.Test.TestApp;

namespace KubeOps.Operator.Web.Test.Webhooks;

public sealed class ValidationWebhookIntegrationTest : IntegrationTestBase
{
    [Fact(Skip = "This test is flakey since localtunnel is not always available. Need an alternative.")]
    public async Task Should_Allow_Creation_Of_Entity()
    {
        using var client = new KubernetesClient.KubernetesClient() as IKubernetesClient;
        var e = await client.CreateAsync(
            new V1OperatorWebIntegrationTestEntity("test-entity", "foobar"),
            TestContext.Current.CancellationToken);
        await client.DeleteAsync(e, TestContext.Current.CancellationToken);
    }

    [Fact(Skip = "This test is flakey since localtunnel is not always available. Need an alternative.")]
    public async Task Should_Disallow_Creation_When_Validation_Fails()
    {
        using var client = new KubernetesClient.KubernetesClient() as IKubernetesClient;
        var ex = await Assert.ThrowsAsync<HttpOperationException>(async () =>
            await client.CreateAsync(
                new V1OperatorWebIntegrationTestEntity("test-entity", "forbidden"),
                TestContext.Current.CancellationToken));
        ex.Message.Should().Contain("name may not be 'forbidden'");
    }
}
