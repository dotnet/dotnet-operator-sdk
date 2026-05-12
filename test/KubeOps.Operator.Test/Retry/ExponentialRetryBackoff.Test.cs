// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Operator.Retry;

namespace KubeOps.Operator.Test.Retry;

public sealed class ExponentialRetryBackoffTest
{
    [Fact]
    public void GetDelayWithJitter_Should_Use_Exponential_Backoff()
    {
        var delay = ExponentialRetryBackoff.GetDelayWithJitter(1);

        delay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
        delay.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void GetDelayWithJitter_Should_Cap_Exponential_Backoff()
    {
        var delay = ExponentialRetryBackoff.GetDelayWithJitter(10);

        delay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(32));
        delay.Should().BeLessThan(TimeSpan.FromSeconds(33));
    }
}
