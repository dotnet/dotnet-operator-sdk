// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.KubernetesClient;

namespace KubeOps.Operator.Web.Test.Webhooks;

public sealed class ExternalWebhookServiceIntegrationTest : IntegrationTestBase
{
    [Fact(
        Skip = ExternalWebhookTestEndpoint.SkipReason,
        SkipUnless = nameof(ExternalWebhookTestEndpoint.IsConfigured),
        SkipType = typeof(ExternalWebhookTestEndpoint))]
    public async Task Should_Install_Validation_Webhooks()
    {
        using var client = new KubernetesClient.KubernetesClient() as IKubernetesClient;
        var validators = await client.GetAsync<V1ValidatingWebhookConfiguration>(
            "dev-validators",
            cancellationToken: TestContext.Current.CancellationToken);

        validators.Should().NotBeNull();
        validators!.Webhooks.Should().Contain(w =>
            w.Name == "validate.weboperatorintegrationtest.weboperator.test.v1" &&
            w.ClientConfig.Url.StartsWith(ExternalUrl) &&
            w.ClientConfig.Url.EndsWith("/validate/v1operatorwebintegrationtestentity"));
    }

    [Fact(
        Skip = ExternalWebhookTestEndpoint.SkipReason,
        SkipUnless = nameof(ExternalWebhookTestEndpoint.IsConfigured),
        SkipType = typeof(ExternalWebhookTestEndpoint))]
    public async Task Should_Install_Mutation_Webhooks()
    {
        using var client = new KubernetesClient.KubernetesClient() as IKubernetesClient;
        var mutators = await client.GetAsync<V1MutatingWebhookConfiguration>(
            "dev-mutators",
            cancellationToken: TestContext.Current.CancellationToken);

        mutators.Should().NotBeNull();
        mutators!.Webhooks.Should().Contain(w =>
            w.Name == "mutate.weboperatorintegrationtest.weboperator.test.v1" &&
            w.ClientConfig.Url.StartsWith(ExternalUrl) &&
            w.ClientConfig.Url.EndsWith("/mutate/v1operatorwebintegrationtestentity"));
    }

    private static string ExternalUrl =>
        new Uri(Environment.GetEnvironmentVariable("KubeOps__Webhooks__ExternalUrl")!).GetLeftPart(UriPartial.Authority);
}
