// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;

namespace KubeOps.Abstractions.Test.Builder;

public sealed class ParallelReconciliationOptionsTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void MaxParallelReconciliations_Should_Throw_For_NonPositive_Value(int value)
    {
        var options = new ParallelReconciliationOptions();

        var act = () => options.MaxParallelReconciliations = value;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MaxParallelReconciliations_Should_Accept_Positive_Value()
    {
        var options = new ParallelReconciliationOptions { MaxParallelReconciliations = 4 };

        options.MaxParallelReconciliations.Should().Be(4);
    }

    [Fact]
    public void GetEffectiveRequeueDelay_Should_Return_Default_When_RequeueDelay_Is_Null()
    {
        var options = new ParallelReconciliationOptions { RequeueDelay = null };

        options.GetEffectiveRequeueDelay().Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetEffectiveRequeueDelay_Should_Return_Configured_Value()
    {
        var configured = TimeSpan.FromSeconds(30);
        var options = new ParallelReconciliationOptions { RequeueDelay = configured };

        options.GetEffectiveRequeueDelay().Should().Be(configured);
    }
}
