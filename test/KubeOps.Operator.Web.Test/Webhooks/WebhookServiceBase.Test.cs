// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Runtime.Versioning;

using FluentAssertions;

using k8s.Models;

using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks;
using KubeOps.Operator.Web.Webhooks.Admission.Validation;
using KubeOps.Operator.Web.Webhooks.Conversion;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace KubeOps.Operator.Web.Test.Webhooks;

[Trait("Area", "WebhookRegistration")]
[RequiresPreviewFeatures]
public sealed class WebhookServiceBaseTest
{
    private const string CrdName = "weboperatorintegrationtests.weboperator.test";

    private static readonly Uri BaseUri = new("https://operator.example.com");

    private readonly CapturingKubernetesClient _client = new();
    private readonly CapturingWebhookConfigurationFactory _factory = new();

    [Theory]
    [InlineData("https://proxy.example.com/my-operator")]
    [InlineData("https://proxy.example.com/my-operator/")]
    public async Task Should_Keep_The_Path_Of_The_Base_Uri(string baseUri)
    {
        await Service(new Uri(baseUri), typeof(TestValidationWebhook))
            .StartAsync(TestContext.Current.CancellationToken);

        _factory.ValidatingRegistrations.Should().ContainSingle().Which.Uri.Should().Be(
            new Uri("https://proxy.example.com/my-operator/validate/v1operatorwebintegrationtestentity"));
    }

    [Fact]
    public async Task Should_Use_The_Route_When_The_Webhook_Attribute_Is_Missing()
    {
        await Service(BaseUri, typeof(RouteOnlyValidator)).StartAsync(TestContext.Current.CancellationToken);

        _factory.ValidatingRegistrations.Should().ContainSingle().Which.Uri.Should().Be(
            new Uri("https://operator.example.com/custom/validate"));
    }

    [Fact]
    public async Task Should_Reject_Webhooks_Without_Webhook_Or_Route_Attribute()
    {
        var start = () => Service(BaseUri, typeof(UnroutedValidator))
            .StartAsync(TestContext.Current.CancellationToken);

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{typeof(UnroutedValidator).FullName}*");
    }

    [Fact]
    public async Task Should_Register_Conversion_Webhooks_On_The_Crd()
    {
        byte[] caBundle = [1, 2, 3];
        _client.Existing[CrdName] = new V1CustomResourceDefinition
        {
            Metadata = new() { Name = CrdName },
            Spec = new(),
        };

        await Service(BaseUri, typeof(TestConverter), caBundle).StartAsync(TestContext.Current.CancellationToken);

        var crd = _client.Updated.Should().ContainSingle()
            .Which.Should().BeOfType<V1CustomResourceDefinition>().Subject;
        crd.Spec.Conversion.Strategy.Should().Be("Webhook");
        crd.Spec.Conversion.Webhook.ConversionReviewVersions.Should().Equal("v1");
        crd.Spec.Conversion.Webhook.ClientConfig.Url.Should()
            .Be("https://operator.example.com/convert/weboperator.test/weboperatorintegrationtests");
        crd.Spec.Conversion.Webhook.ClientConfig.CaBundle.Should().Equal(caBundle);
    }

    [Fact]
    public async Task Should_Skip_Conversion_Webhooks_Without_Crd()
    {
        await Service(BaseUri, typeof(TestConverter)).StartAsync(TestContext.Current.CancellationToken);

        _client.Updated.Should().BeEmpty();
    }

    private ExternalWebhookService Service(Uri baseUri, Type webhook, byte[]? caBundle = null) => new(
        NullLogger<ExternalWebhookService>.Instance,
        _client,
        new([webhook.GetTypeInfo()]),
        new(baseUri, caBundle),
        _factory);

    [Route("/custom/validate")]
    private sealed class RouteOnlyValidator : ValidationWebhook<V1OperatorWebIntegrationTestEntity>;

    private sealed class UnroutedValidator : ValidationWebhook<V1OperatorWebIntegrationTestEntity>;

    [ConversionWebhook(typeof(V1OperatorWebIntegrationTestEntity))]
    private sealed class TestConverter : ConversionWebhook<V1OperatorWebIntegrationTestEntity>
    {
        protected override IEnumerable<IEntityConverter<V1OperatorWebIntegrationTestEntity>> Converters => [];
    }
}
