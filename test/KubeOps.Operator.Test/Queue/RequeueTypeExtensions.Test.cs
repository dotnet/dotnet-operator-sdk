// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;

using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.Operator.Queue;

namespace KubeOps.Operator.Test.Queue;

public sealed class RequeueTypeExtensionsTest
{
    [Theory]
    [InlineData(WatchEventType.Added, RequeueType.Added)]
    [InlineData(WatchEventType.Modified, RequeueType.Modified)]
    [InlineData(WatchEventType.Deleted, RequeueType.Deleted)]
    public void ToRequeueType_Should_Convert_WatchEventType_Correctly(
        WatchEventType watchEventType,
        RequeueType expectedRequeueType)
    {
        var result = watchEventType.ToRequeueType();

        result.Should().Be(expectedRequeueType);
    }

    [Theory]
    [InlineData(RequeueType.Added, WatchEventType.Added)]
    [InlineData(RequeueType.Modified, WatchEventType.Modified)]
    [InlineData(RequeueType.Deleted, WatchEventType.Deleted)]
    public void ToWatchEventType_Should_Convert_RequeueType_Correctly(
        RequeueType requeueType,
        WatchEventType expectedWatchEventType)
    {
        var result = requeueType.ToWatchEventType();

        result.Should().Be(expectedWatchEventType);
    }

    [Fact]
    public void ToRequeueType_Should_Throw_For_Unsupported_WatchEventType()
    {
        var unsupportedType = (WatchEventType)999;

        Action act = () => unsupportedType.ToRequeueType();

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*WatchEventType*999*not supported*");
    }

    [Fact]
    public void ToWatchEventType_Should_Throw_For_Unsupported_RequeueType()
    {
        var unsupportedType = (RequeueType)999;

        Action act = () => unsupportedType.ToWatchEventType();

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*RequeueType*999*not supported*");
    }

    [Theory]
    [InlineData(WatchEventType.Added)]
    [InlineData(WatchEventType.Modified)]
    [InlineData(WatchEventType.Deleted)]
    public void Bidirectional_Conversion_Should_Be_Reversible_From_WatchEventType(WatchEventType original)
    {
        var requeueType = original.ToRequeueType();
        var converted = requeueType.ToWatchEventType();

        converted.Should().Be(original);
    }

    [Theory]
    [InlineData(RequeueType.Added)]
    [InlineData(RequeueType.Modified)]
    [InlineData(RequeueType.Deleted)]
    public void Bidirectional_Conversion_Should_Be_Reversible_From_RequeueType(RequeueType original)
    {
        var watchEventType = original.ToWatchEventType();
        var converted = watchEventType.ToRequeueType();

        converted.Should().Be(original);
    }

    [Fact]
    public void ToRequeueType_Should_Handle_Added_EventType()
    {
        var eventType = WatchEventType.Added;

        var result = eventType.ToRequeueType();

        result.Should().Be(RequeueType.Added);
    }

    [Fact]
    public void ToRequeueType_Should_Handle_Modified_EventType()
    {
        var eventType = WatchEventType.Modified;

        var result = eventType.ToRequeueType();

        result.Should().Be(RequeueType.Modified);
    }

    [Fact]
    public void ToRequeueType_Should_Handle_Deleted_EventType()
    {
        var eventType = WatchEventType.Deleted;

        var result = eventType.ToRequeueType();

        result.Should().Be(RequeueType.Deleted);
    }

    [Fact]
    public void ToWatchEventType_Should_Handle_Added_RequeueType()
    {
        var requeueType = RequeueType.Added;

        var result = requeueType.ToWatchEventType();

        result.Should().Be(WatchEventType.Added);
    }

    [Fact]
    public void ToWatchEventType_Should_Handle_Modified_RequeueType()
    {
        var requeueType = RequeueType.Modified;

        var result = requeueType.ToWatchEventType();

        result.Should().Be(WatchEventType.Modified);
    }

    [Fact]
    public void ToWatchEventType_Should_Handle_Deleted_RequeueType()
    {
        var requeueType = RequeueType.Deleted;

        var result = requeueType.ToWatchEventType();

        result.Should().Be(WatchEventType.Deleted);
    }

    [Fact]
    public void All_RequeueTypes_Should_Have_Corresponding_WatchEventType()
    {
        var allRequeueTypes = Enum.GetValues<RequeueType>();

        // Act & Assert
        foreach (var requeueType in allRequeueTypes)
        {
            Action act = () => requeueType.ToWatchEventType();
            act.Should().NotThrow($"RequeueType.{requeueType} should have a corresponding WatchEventType");
        }
    }

    [Fact]
    public void Supported_WatchEventTypes_Should_Have_Corresponding_RequeueType()
    {
        var supportedWatchEventTypes = new[] { WatchEventType.Added, WatchEventType.Modified, WatchEventType.Deleted };

        // Act & Assert
        foreach (var watchEventType in supportedWatchEventTypes)
        {
            Action act = () => watchEventType.ToRequeueType();
            act.Should().NotThrow($"WatchEventType.{watchEventType} should have a corresponding RequeueType");
        }
    }
}
