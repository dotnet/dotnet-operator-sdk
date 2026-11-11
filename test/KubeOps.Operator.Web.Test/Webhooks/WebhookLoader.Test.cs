// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Runtime.Versioning;

using FluentAssertions;

using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks;
using KubeOps.Operator.Web.Webhooks.Admission.Mutation;
using KubeOps.Operator.Web.Webhooks.Admission.Validation;
using KubeOps.Operator.Web.Webhooks.Conversion;

namespace KubeOps.Operator.Web.Test.Webhooks;

[RequiresPreviewFeatures]
[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Webhooks)]
public sealed class WebhookLoaderTest
{
    [Fact]
    public void Should_Find_Webhooks_By_Their_Base_Class()
    {
        var loader = Loader(typeof(DirectValidator), typeof(DirectMutator), typeof(DirectConverter));

        loader.ValidationWebhooks.Should().ContainSingle()
            .Which.Should().Be(new WebhookType(typeof(DirectValidator).GetTypeInfo(), typeof(Entity)));
        loader.MutationWebhooks.Should().ContainSingle()
            .Which.Should().Be(new WebhookType(typeof(DirectMutator).GetTypeInfo(), typeof(Entity)));
        loader.ConversionWebhooks.Should().ContainSingle()
            .Which.Should().Be(new WebhookType(typeof(DirectConverter).GetTypeInfo(), typeof(Entity)));
    }

    [Fact]
    public void Should_Find_Webhooks_Derived_From_An_Intermediate_Base_Class()
    {
        var loader = Loader(typeof(AuditedValidator<>), typeof(IndirectValidator));

        loader.ValidationWebhooks.Should().ContainSingle()
            .Which.Should().Be(new WebhookType(typeof(IndirectValidator).GetTypeInfo(), typeof(Entity)));
    }

    [Fact]
    public void Should_Ignore_Abstract_Webhooks()
    {
        var loader = Loader(typeof(AbstractValidator));

        loader.ValidationWebhooks.Should().BeEmpty();
    }

    [Fact]
    public void Should_Ignore_Types_That_Are_No_Webhooks()
    {
        var loader = Loader(typeof(Entity), typeof(WebhookLoaderTest));

        loader.ValidationWebhooks.Should().BeEmpty();
        loader.MutationWebhooks.Should().BeEmpty();
        loader.ConversionWebhooks.Should().BeEmpty();
    }

    private static WebhookLoader Loader(params Type[] types) => new(types.Select(t => t.GetTypeInfo()));

    private sealed class Entity : V1OperatorWebIntegrationTestEntity;

    private sealed class DirectValidator : ValidationWebhook<Entity>;

    private sealed class DirectMutator : MutationWebhook<Entity>;

    private sealed class DirectConverter : ConversionWebhook<Entity>
    {
        protected override IEnumerable<IEntityConverter<Entity>> Converters => [];
    }

    private abstract class AuditedValidator<TEntity> : ValidationWebhook<TEntity>
        where TEntity : k8s.IKubernetesObject<k8s.Models.V1ObjectMeta>;

    private sealed class IndirectValidator : AuditedValidator<Entity>;

    private abstract class AbstractValidator : ValidationWebhook<Entity>;
}
