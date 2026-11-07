// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.KubernetesClient;
using KubeOps.Operator.Web.Test.TestApp;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission.Mutation;

[Trait("Area", "AdmissionWebhook")]
public sealed class MutationWebhookIntegrationTest : IntegrationTestBase
{
    [WebhookIntegrationFact]
    public async Task Should_Allow_Creation_Of_Entity()
    {
        using var client = new KubernetesClient.KubernetesClient() as IKubernetesClient;
        var e = await client.CreateAsync(
            new V1OperatorWebIntegrationTestEntity("test-entity", "foobar"),
            TestContext.Current.CancellationToken);
        e.Spec.Username.Should().Be("foobar");
        await client.DeleteAsync(e, TestContext.Current.CancellationToken);
    }

    [WebhookIntegrationFact]
    public async Task Should_Mutate_Entity_According_To_Code()
    {
        using var client = new KubernetesClient.KubernetesClient() as IKubernetesClient;
        var e = await client.CreateAsync(
            new V1OperatorWebIntegrationTestEntity("test-entity", "overwrite"),
            TestContext.Current.CancellationToken);
        e.Spec.Username.Should().Be("overwritten");
        await client.DeleteAsync(e, TestContext.Current.CancellationToken);
    }
}
