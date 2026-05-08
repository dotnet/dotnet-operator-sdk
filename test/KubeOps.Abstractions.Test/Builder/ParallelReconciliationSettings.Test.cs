// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;

namespace KubeOps.Abstractions.Test.Builder;

public sealed class ParallelReconciliationSettingsTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void MaxParallelReconciliations_Should_Throw_For_NonPositive_Value(int value)
    {
        var builder = new ParallelReconciliationSettingsBuilder();

        var act = () => builder.MaxParallelReconciliations = value;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MaxParallelReconciliations_Should_Accept_Positive_Value()
    {
        var settings = new ParallelReconciliationSettingsBuilder { MaxParallelReconciliations = 4 }.Build();

        settings.MaxParallelReconciliations.Should().Be(4);
    }

    [Fact]
    public void GetEffectiveRequeueDelay_Should_Return_Default_When_RequeueDelay_Is_Null()
    {
        var settings = new ParallelReconciliationSettingsBuilder { RequeueDelay = null }.Build();

        settings.GetEffectiveRequeueDelay().Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetEffectiveRequeueDelay_Should_Return_Configured_Value()
    {
        var configured = TimeSpan.FromSeconds(30);
        var settings = new ParallelReconciliationSettingsBuilder { RequeueDelay = configured }.Build();

        settings.GetEffectiveRequeueDelay().Should().Be(configured);
    }

    [Fact]
    public void MaxErrorRetries_Should_Default_To_Five()
    {
        var builder = new ParallelReconciliationSettingsBuilder();

        builder.MaxErrorRetries.Should().Be(5);
    }

    [Fact]
    public void ErrorBackoffBase_Should_Default_To_Two_Seconds()
    {
        var builder = new ParallelReconciliationSettingsBuilder();

        builder.ErrorBackoffBase.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(1, 2)]   // base * 2^0 = 2 s
    [InlineData(2, 4)]   // base * 2^1 = 4 s
    [InlineData(3, 8)]   // base * 2^2 = 8 s
    [InlineData(4, 16)]  // base * 2^3 = 16 s
    public void GetErrorBackoffDelay_Should_Return_Exponential_Backoff(int retryCount, double expectedSeconds)
    {
        var settings = new ParallelReconciliationSettingsBuilder { ErrorBackoffBase = TimeSpan.FromSeconds(2) }.Build();

        settings.GetErrorBackoffDelay(retryCount).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Fluent_Extensions_Set_All_Properties()
    {
        var settings = new ParallelReconciliationSettingsBuilder()
            .WithMaxParallelReconciliations(10)
            .WithConflictStrategy(ParallelReconciliationConflictStrategy.Discard)
            .WithRequeueDelay(TimeSpan.FromSeconds(3))
            .WithMaxErrorRetries(2)
            .WithErrorBackoffBase(TimeSpan.FromSeconds(1))
            .Build();

        settings.MaxParallelReconciliations.Should().Be(10);
        settings.ConflictStrategy.Should().Be(ParallelReconciliationConflictStrategy.Discard);
        settings.RequeueDelay.Should().Be(TimeSpan.FromSeconds(3));
        settings.MaxErrorRetries.Should().Be(2);
        settings.ErrorBackoffBase.Should().Be(TimeSpan.FromSeconds(1));
    }
}
