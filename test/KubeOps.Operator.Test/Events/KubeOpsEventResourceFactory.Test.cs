// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Events;
using KubeOps.Operator.Events;
using KubeOps.Operator.Test.TestEntities;

namespace KubeOps.Operator.Test.Events;

public sealed class KubeOpsEventResourceFactoryTest
{
    private readonly OperatorSettings _settings = new OperatorSettingsBuilder { Name = "test-operator" }.Build();

    [Fact]
    public void Should_Create_Event_With_Correct_Properties()
    {
        var factory = new KubeOpsEventResourceFactory(_settings);

        var entity = new V1OperatorIntegrationTestEntity(
            "test-entity",
            "user",
            "default") { Metadata = { Uid = "test-uid" } };

        var result = factory.CreateEvent(
            entity,
            "TestReason",
            "Test message",
            EventType.Normal);

        result.Should().NotBeNull();
        result.Reason.Should().Be("TestReason");
        result.Message.Should().Be("Test message");
        result.Type.Should().Be("Normal");
        result.ReportingComponent.Should().Be("test-operator");
        result.Source.Component.Should().Be("test-operator");
        result.ReportingInstance.Should().Be(Environment.MachineName);
        result.Metadata.NamespaceProperty.Should().Be("default");
    }

    [Fact]
    public void Should_Default_Namespace_When_Not_Set()
    {
        var factory = new KubeOpsEventResourceFactory(_settings);
        var entity =
            new V1OperatorIntegrationTestEntity { ApiVersion = "operator.test/v1", Kind = "OperatorIntegrationTest" };
        entity.Metadata.Name = "test";
        entity.Metadata.Uid = "test-uid";

        var result = factory.CreateEvent(
            entity,
            "Reason",
            "Message",
            EventType.Normal);

        result.Metadata.NamespaceProperty.Should().Be("default");
    }
}
