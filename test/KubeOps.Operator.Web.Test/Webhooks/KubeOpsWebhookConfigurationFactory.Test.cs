// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Operator.Web.Webhooks;

namespace KubeOps.Operator.Web.Test.Webhooks;

[Trait("Area", "WebhookRegistration")]
public sealed class KubeOpsWebhookConfigurationFactoryTest
{
    private static readonly EntityMetadata Metadata = new("TestEntity", "v1", "test.kubeops.dev", "testentities");
    private static readonly Uri WebhookUri = new("https://operator.example.com/hook/testentity");
    private static readonly EntityMetadata OtherPluralOfSameKind = Metadata with { Plural = "others" };
    private static readonly byte[] CaBundle = [1, 2, 3];

    private readonly KubeOpsWebhookConfigurationFactory _factory = new();

    [Fact]
    public void Should_Create_Validating_Configuration()
    {
        var config = _factory.CreateValidatingConfiguration([new(Metadata, WebhookUri, CaBundle)]);

        config.Metadata.Name.Should().Be(KubeOpsWebhookConfigurationFactory.ValidatingConfigurationName);
        var webhook = config.Webhooks.Should().ContainSingle().Subject;
        webhook.Name.Should().Be("validate.testentity.test.kubeops.dev.v1");
        webhook.MatchPolicy.Should().Be("Exact");
        webhook.SideEffects.Should().Be("None");
        webhook.AdmissionReviewVersions.Should().Equal("v1");
        AssertClientConfig(webhook.ClientConfig);
        AssertRules(webhook.Rules);
    }

    [Fact]
    public void Should_Create_Mutating_Configuration()
    {
        var config = _factory.CreateMutatingConfiguration([new(Metadata, WebhookUri, CaBundle)]);

        config.Metadata.Name.Should().Be(KubeOpsWebhookConfigurationFactory.MutatingConfigurationName);
        var webhook = config.Webhooks.Should().ContainSingle().Subject;
        webhook.Name.Should().Be("mutate.testentity.test.kubeops.dev.v1");
        webhook.MatchPolicy.Should().Be("Exact");
        webhook.SideEffects.Should().Be("None");
        webhook.AdmissionReviewVersions.Should().Equal("v1");
        AssertClientConfig(webhook.ClientConfig);
        AssertRules(webhook.Rules);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Should_Name_Webhooks_Of_The_Core_Group_After_Core(string? group)
    {
        var metadata = new EntityMetadata("ConfigMap", "v1", group);

        _factory.CreateValidatingConfiguration([new(metadata, WebhookUri, null)])
            .Webhooks.Should().ContainSingle().Which.Name.Should().Be("validate.configmap.core.v1");
        _factory.CreateMutatingConfiguration([new(metadata, WebhookUri, null)])
            .Webhooks.Should().ContainSingle().Which.Name.Should().Be("mutate.configmap.core.v1");
    }

    [Fact]
    public void Should_Reject_Validation_Webhooks_With_The_Same_Name()
    {
        var create = () => _factory.CreateValidatingConfiguration(
            [new(Metadata, WebhookUri, null), new(OtherPluralOfSameKind, WebhookUri, null)]);

        create.Should().Throw<InvalidOperationException>().WithMessage("*validate.testentity.test.kubeops.dev.v1*");
    }

    [Fact]
    public void Should_Reject_Mutation_Webhooks_With_The_Same_Name()
    {
        var create = () => _factory.CreateMutatingConfiguration(
            [new(Metadata, WebhookUri, null), new(OtherPluralOfSameKind, WebhookUri, null)]);

        create.Should().Throw<InvalidOperationException>().WithMessage("*mutate.testentity.test.kubeops.dev.v1*");
    }

    private static void AssertClientConfig(Admissionregistrationv1WebhookClientConfig clientConfig)
    {
        clientConfig.Url.Should().Be(WebhookUri.ToString());
        clientConfig.CaBundle.Should().Equal(CaBundle);
    }

    private static void AssertRules(IEnumerable<V1RuleWithOperations> rules)
    {
        var rule = rules.Should().ContainSingle().Subject;
        rule.Operations.Should().Equal("*");
        rule.ApiGroups.Should().Equal("test.kubeops.dev");
        rule.ApiVersions.Should().Equal("v1");
        rule.Resources.Should().Equal("testentities");
    }
}
