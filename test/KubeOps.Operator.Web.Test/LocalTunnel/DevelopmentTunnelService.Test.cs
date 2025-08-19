// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.KubernetesClient;

namespace KubeOps.Operator.Web.Test.LocalTunnel;

public class DevelopmentTunnelServiceTest : IntegrationTestBase
{
    [Fact(Skip = "This test is flakey since localtunnel is not always available. Need an alternative.")]
    public async Task Should_Install_Validation_Webhooks()
    {
        using var client = new KubernetesClient.KubernetesClient() as IKubernetesClient;
        var validators = await client.GetAsync<V1ValidatingWebhookConfiguration>("dev-validators");
        validators.Should().NotBeNull();
        validators!.Webhooks.Should().HaveCount(1);
        validators.Webhooks[0].Name.Should().Be("validate.weboperatorintegrationtest.weboperator.test.v1");
        validators.Webhooks[0].ClientConfig.Url.Should().Contain("/validate/v1operatorwebintegrationtestentity");
    }

    [Fact(Skip = "This test is flakey since localtunnel is not always available. Need an alternative.")]
    public async Task Should_Install_Mutation_Webhooks()
    {
        using var client = new KubernetesClient.KubernetesClient() as IKubernetesClient;
        var mutators = await client.GetAsync<V1ValidatingWebhookConfiguration>("dev-mutators");
        mutators.Should().NotBeNull();
        mutators!.Webhooks.Should().HaveCount(1);
        mutators.Webhooks[0].Name.Should().Be("mutate.weboperatorintegrationtest.weboperator.test.v1");
        mutators.Webhooks[0].ClientConfig.Url.Should().Contain("/mutate/v1operatorwebintegrationtestentity");
    }
}
