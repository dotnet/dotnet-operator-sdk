// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;

using KubeOps.Abstractions.Reconciliation;
using KubeOps.Operator.Reconciliation;

namespace KubeOps.Operator.Test.Reconciliation;

public sealed class ReconciliationTypeExtensionsTest
{
    [Theory]
    [InlineData(WatchEventType.Added, ReconciliationType.Added)]
    [InlineData(WatchEventType.Modified, ReconciliationType.Modified)]
    [InlineData(WatchEventType.Deleted, ReconciliationType.Deleted)]
    public void ToReconciliationType_Should_Convert_WatchEventType_Correctly(
        WatchEventType watchEventType,
        ReconciliationType expectedReconciliationType)
    {
        var result = watchEventType.ToReconciliationType();

        result.Should().Be(expectedReconciliationType);
    }

    [Fact]
    public void ToReconciliationType_Should_Throw_For_Unsupported_WatchEventType()
    {
        const WatchEventType unsupportedType = (WatchEventType)999;

        Action act = () => unsupportedType.ToReconciliationType();

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*WatchEventType*999*not supported*");
    }

    [Fact]
    public void ToReconciliationType_Should_Handle_Added_EventType()
    {
        const WatchEventType eventType = WatchEventType.Added;

        var result = eventType.ToReconciliationType();

        result.Should().Be(ReconciliationType.Added);
    }

    [Fact]
    public void ToReconciliationType_Should_Handle_Modified_EventType()
    {
        const WatchEventType eventType = WatchEventType.Modified;

        var result = eventType.ToReconciliationType();

        result.Should().Be(ReconciliationType.Modified);
    }

    [Fact]
    public void ToReconciliationType_Should_Handle_Deleted_EventType()
    {
        const WatchEventType eventType = WatchEventType.Deleted;

        var result = eventType.ToReconciliationType();

        result.Should().Be(ReconciliationType.Deleted);
    }

    [Fact]
    public void Supported_WatchEventTypes_Should_Have_Corresponding_ReconciliationType()
    {
        var supportedWatchEventTypes = new[] { WatchEventType.Added, WatchEventType.Modified, WatchEventType.Deleted };

        // Act & Assert
        foreach (var watchEventType in supportedWatchEventTypes)
        {
            Action act = () => watchEventType.ToReconciliationType();
            act.Should().NotThrow($"WatchEventType.{watchEventType} should have a corresponding ReconciliationType");
        }
    }
}
