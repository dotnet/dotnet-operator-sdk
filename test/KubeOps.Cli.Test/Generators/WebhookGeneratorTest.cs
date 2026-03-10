// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Cli.Generators;
using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;

using Spectre.Console.Testing;

namespace KubeOps.Cli.Test.Generators;

public class WebhookGeneratorTest
{
    private class FakeEntity;

    private abstract class FakeValidationWebhookBase<T>
    {
        public virtual void Create(T entity, bool dryRun)
        {
        }
    }

    private class TestValidator : FakeValidationWebhookBase<FakeEntity>
    {
        public override void Create(FakeEntity entity, bool dryRun)
        {
        }
    }

    private abstract class FakeMutationWebhookBase<T>
    {
        public virtual void Create(T entity, bool dryRun)
        {
        }
    }

    private class TestMutator : FakeMutationWebhookBase<FakeEntity>
    {
        public override void Create(FakeEntity entity, bool dryRun)
        {
        }
    }

    [Fact]
    public void ValidationWebhookGenerator_Should_Set_Namespace_On_Service()
    {
        var metadata = new EntityMetadata("TestEntity", "v1", "testing.dev");
        var webhooks = new List<ValidationWebhook>
        {
            new(typeof(TestValidator).GetTypeInfo(), metadata),
        };
        var caBundle = "test-ca"u8.ToArray();
        var output = new ResultOutput(new TestConsole(), OutputFormat.Yaml);

        new ValidationWebhookGenerator(webhooks, caBundle, OutputFormat.Yaml).Generate(output);

        var config = (V1ValidatingWebhookConfiguration)output["validators.yaml"];
        config.Webhooks.Should().ContainSingle();

        var webhook = config.Webhooks[0];
        webhook.ClientConfig.Service.Should().NotBeNull();
        webhook.ClientConfig.Service.NamespaceProperty.Should().Be("system");
        webhook.ClientConfig.Service.Name.Should().Be("operator");
    }

    [Fact]
    public void MutationWebhookGenerator_Should_Set_Namespace_On_Service()
    {
        var metadata = new EntityMetadata("TestEntity", "v1", "testing.dev");
        var webhooks = new List<MutationWebhook>
        {
            new(typeof(TestMutator).GetTypeInfo(), metadata),
        };
        var caBundle = "test-ca"u8.ToArray();
        var output = new ResultOutput(new TestConsole(), OutputFormat.Yaml);

        new MutationWebhookGenerator(webhooks, caBundle, OutputFormat.Yaml).Generate(output);

        var config = (V1MutatingWebhookConfiguration)output["mutators.yaml"];
        config.Webhooks.Should().ContainSingle();

        var webhook = config.Webhooks[0];
        webhook.ClientConfig.Service.Should().NotBeNull();
        webhook.ClientConfig.Service.NamespaceProperty.Should().Be("system");
        webhook.ClientConfig.Service.Name.Should().Be("operator");
    }
}
