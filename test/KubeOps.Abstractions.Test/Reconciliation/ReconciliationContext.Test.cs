// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Abstractions.Test.Reconciliation;

public sealed class ReconciliationContextTest
{
    [Theory]
    [InlineData(ReconciliationType.Added)]
    [InlineData(ReconciliationType.Modified)]
    [InlineData(ReconciliationType.Deleted)]
    public void CreateFor_Should_Create_Context_With_ApiServer_TriggerSource(ReconciliationType eventType)
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFor(entity, eventType, ReconciliationTriggerSource.ApiServer);

        context.Entity.Should().Be(entity);
        context.EventType.Should().Be(eventType);
        context.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.ApiServer);
    }

    [Theory]
    [InlineData(ReconciliationType.Added)]
    [InlineData(ReconciliationType.Modified)]
    [InlineData(ReconciliationType.Deleted)]
    public void CreateFor_Should_Create_Context_With_Operator_TriggerSource(ReconciliationType eventType)
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFor(entity, eventType, ReconciliationTriggerSource.Operator);

        context.Entity.Should().Be(entity);
        context.EventType.Should().Be(eventType);
        context.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.Operator);
    }

    [Fact]
    public void IsTriggeredByApiServer_Should_Return_True_For_ApiServer_Context()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Added, ReconciliationTriggerSource.ApiServer);

        var isTriggeredByApiServer = context.IsTriggeredByApiServer();
        var isTriggeredByOperator = context.IsTriggeredByOperator();

        isTriggeredByApiServer.Should().BeTrue();
        isTriggeredByOperator.Should().BeFalse();
    }

    [Fact]
    public void IsTriggeredByOperator_Should_Return_True_For_Operator_Context()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Modified, ReconciliationTriggerSource.Operator);

        var isTriggeredByOperator = context.IsTriggeredByOperator();
        var isTriggeredByApiServer = context.IsTriggeredByApiServer();

        isTriggeredByOperator.Should().BeTrue();
        isTriggeredByApiServer.Should().BeFalse();
    }

    [Fact]
    public void Record_Equality_Should_Work_For_Same_Values()
    {
        var entity = CreateTestEntity("test-entity");

        var context1 = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Added, ReconciliationTriggerSource.Operator);
        var context2 = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Added, ReconciliationTriggerSource.Operator);

        context1.Should().NotBeSameAs(context2);
        context1.Entity.Should().BeSameAs(context2.Entity);
        context1.EventType.Should().Be(context2.EventType);
        context1.ReconciliationTriggerSource.Should().Be(context2.ReconciliationTriggerSource);
    }

    [Fact]
    public void Contexts_With_Different_EventTypes_Should_Have_Different_EventTypes()
    {
        var entity = CreateTestEntity();

        var contextAdded = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Added, ReconciliationTriggerSource.ApiServer);
        var contextModified = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Modified, ReconciliationTriggerSource.ApiServer);
        var contextDeleted = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Deleted, ReconciliationTriggerSource.ApiServer);

        contextAdded.EventType.Should().Be(ReconciliationType.Added);
        contextModified.EventType.Should().Be(ReconciliationType.Modified);
        contextDeleted.EventType.Should().Be(ReconciliationType.Deleted);
    }

    [Fact]
    public void Contexts_With_Different_TriggerSources_Should_Have_Different_TriggerSources()
    {
        var entity = CreateTestEntity();

        var apiServerContext = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Added, ReconciliationTriggerSource.ApiServer);
        var operatorContext = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Added, ReconciliationTriggerSource.Operator);

        apiServerContext.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.ApiServer);
        operatorContext.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.Operator);
        apiServerContext.ReconciliationTriggerSource.Should().NotBe(operatorContext.ReconciliationTriggerSource);
    }

    [Fact]
    public void Context_Should_Contain_Entity_Metadata()
    {
        var entity = CreateTestEntity("test-configmap", "test-namespace");

        var context = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Added, ReconciliationTriggerSource.ApiServer);

        context.Entity.Metadata.Name.Should().Be("test-configmap");
        context.Entity.Metadata.NamespaceProperty.Should().Be("test-namespace");
    }

    [Theory]
    [InlineData(ReconciliationTriggerSource.ApiServer, ReconciliationType.Added)]
    [InlineData(ReconciliationTriggerSource.ApiServer, ReconciliationType.Modified)]
    [InlineData(ReconciliationTriggerSource.ApiServer, ReconciliationType.Deleted)]
    [InlineData(ReconciliationTriggerSource.Operator, ReconciliationType.Added)]
    [InlineData(ReconciliationTriggerSource.Operator, ReconciliationType.Modified)]
    [InlineData(ReconciliationTriggerSource.Operator, ReconciliationType.Deleted)]
    public void Context_Should_Support_All_Combinations_Of_TriggerSource_And_EventType(
        ReconciliationTriggerSource triggerSource,
        ReconciliationType eventType)
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFor(entity, eventType, triggerSource);

        context.ReconciliationTriggerSource.Should().Be(triggerSource);
        context.EventType.Should().Be(eventType);
    }

    [Fact]
    public void Multiple_Contexts_With_Same_Entity_Should_Share_Entity_Reference()
    {
        var entity = CreateTestEntity();

        var context1 = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Added, ReconciliationTriggerSource.ApiServer);
        var context2 = ReconciliationContext<V1ConfigMap>.CreateFor(entity, ReconciliationType.Modified, ReconciliationTriggerSource.Operator);

        context1.Entity.Should().BeSameAs(context2.Entity);
    }

    private static V1ConfigMap CreateTestEntity(string? name = null, string? ns = null)
        => new()
        {
            Metadata = new()
            {
                Name = name ?? "test-configmap",
                NamespaceProperty = ns ?? "default",
                Uid = Guid.NewGuid().ToString(),
            },
        };
}
