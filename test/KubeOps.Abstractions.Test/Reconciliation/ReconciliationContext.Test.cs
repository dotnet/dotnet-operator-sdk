// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Abstractions.Test.Reconciliation;

public sealed class ReconciliationContextTest
{
    [Fact]
    public void CreateFromApiServerEvent_Should_Create_Context_With_ApiServer_TriggerSource()
    {
        var entity = CreateTestEntity();
        const WatchEventType eventType = WatchEventType.Added;

        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, eventType);

        context.Entity.Should().Be(entity);
        context.EventType.Should().Be(eventType);
        context.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.ApiServer);
    }

    [Fact]
    public void CreateFromOperatorEvent_Should_Create_Context_With_Operator_TriggerSource()
    {
        var entity = CreateTestEntity();
        const WatchEventType eventType = WatchEventType.Modified;

        var context = ReconciliationContext<V1ConfigMap>.CreateFromOperatorEvent(entity, eventType);

        context.Entity.Should().Be(entity);
        context.EventType.Should().Be(eventType);
        context.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.Operator);
    }

    [Theory]
    [InlineData(WatchEventType.Added)]
    [InlineData(WatchEventType.Modified)]
    [InlineData(WatchEventType.Deleted)]
    public void CreateFromApiServerEvent_Should_Support_All_WatchEventTypes(WatchEventType eventType)
    {
        var entity = CreateTestEntity();

        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, eventType);

        context.EventType.Should().Be(eventType);
        context.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.ApiServer);
    }

    [Theory]
    [InlineData(WatchEventType.Added)]
    [InlineData(WatchEventType.Modified)]
    [InlineData(WatchEventType.Deleted)]
    public void CreateFromOperatorEvent_Should_Support_All_WatchEventTypes(WatchEventType eventType)
    {
        var entity = CreateTestEntity();

        var context = ReconciliationContext<V1ConfigMap>.CreateFromOperatorEvent(entity, eventType);

        context.EventType.Should().Be(eventType);
        context.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.Operator);
    }

    [Fact]
    public void IsTriggeredByApiServer_Should_Return_True_For_ApiServer_Context()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);

        var isTriggeredByApiServer = context.IsTriggeredByApiServer();
        var isTriggeredByOperator = context.IsTriggeredByOperator();

        isTriggeredByApiServer.Should().BeTrue();
        isTriggeredByOperator.Should().BeFalse();
    }

    [Fact]
    public void IsTriggeredByOperator_Should_Return_True_For_Operator_Context()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromOperatorEvent(entity, WatchEventType.Modified);

        var isTriggeredByOperator = context.IsTriggeredByOperator();
        var isTriggeredByApiServer = context.IsTriggeredByApiServer();

        isTriggeredByOperator.Should().BeTrue();
        isTriggeredByApiServer.Should().BeFalse();
    }

    [Fact]
    public void Record_Equality_Should_Work_For_Same_Values()
    {
        var entity = CreateTestEntity("test-entity");

        var context1 = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        var context2 = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);

        context1.Should().NotBeSameAs(context2);
        context1.Entity.Should().BeSameAs(context2.Entity);
        context1.EventType.Should().Be(context2.EventType);
        context1.ReconciliationTriggerSource.Should().Be(context2.ReconciliationTriggerSource);
    }

    [Fact]
    public void Contexts_With_Different_EventTypes_Should_Have_Different_EventTypes()
    {
        var entity = CreateTestEntity();

        var contextAdded = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        var contextModified = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Modified);
        var contextDeleted = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Deleted);

        contextAdded.EventType.Should().Be(WatchEventType.Added);
        contextModified.EventType.Should().Be(WatchEventType.Modified);
        contextDeleted.EventType.Should().Be(WatchEventType.Deleted);
    }

    [Fact]
    public void Contexts_With_Different_TriggerSources_Should_Have_Different_TriggerSources()
    {
        var entity = CreateTestEntity();

        var apiServerContext = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        var operatorContext = ReconciliationContext<V1ConfigMap>.CreateFromOperatorEvent(entity, WatchEventType.Added);

        apiServerContext.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.ApiServer);
        operatorContext.ReconciliationTriggerSource.Should().Be(ReconciliationTriggerSource.Operator);
        apiServerContext.ReconciliationTriggerSource.Should().NotBe(operatorContext.ReconciliationTriggerSource);
    }

    [Fact]
    public void Context_Should_Contain_Entity_Metadata()
    {
        var entity = CreateTestEntity("test-configmap", "test-namespace");

        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);

        context.Entity.Metadata.Name.Should().Be("test-configmap");
        context.Entity.Metadata.NamespaceProperty.Should().Be("test-namespace");
    }

    [Theory]
    [InlineData(ReconciliationTriggerSource.ApiServer, WatchEventType.Added)]
    [InlineData(ReconciliationTriggerSource.ApiServer, WatchEventType.Modified)]
    [InlineData(ReconciliationTriggerSource.ApiServer, WatchEventType.Deleted)]
    [InlineData(ReconciliationTriggerSource.Operator, WatchEventType.Added)]
    [InlineData(ReconciliationTriggerSource.Operator, WatchEventType.Modified)]
    [InlineData(ReconciliationTriggerSource.Operator, WatchEventType.Deleted)]
    public void Context_Should_Support_All_Combinations_Of_TriggerSource_And_EventType(
        ReconciliationTriggerSource triggerSource,
        WatchEventType eventType)
    {
        var entity = CreateTestEntity();

        var context = triggerSource == ReconciliationTriggerSource.ApiServer
            ? ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, eventType)
            : ReconciliationContext<V1ConfigMap>.CreateFromOperatorEvent(entity, eventType);

        context.ReconciliationTriggerSource.Should().Be(triggerSource);
        context.EventType.Should().Be(eventType);
    }

    [Fact]
    public void Multiple_Contexts_With_Same_Entity_Should_Share_Entity_Reference()
    {
        var entity = CreateTestEntity();

        var context1 = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        var context2 = ReconciliationContext<V1ConfigMap>.CreateFromOperatorEvent(entity, WatchEventType.Modified);

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
