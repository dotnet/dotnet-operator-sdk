// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;

namespace KubeOps.Abstractions.Test.Builder;

public sealed class OperatorSettingsReconcileStrategyTest
{
    [Fact]
    public void ReconcileStrategy_Should_Default_To_ByGeneration()
    {
        var settings = new OperatorSettings();
        settings.ReconcileStrategy.Should().Be(ReconcileStrategy.ByGeneration);
    }

    [Theory]
    [InlineData(ReconcileStrategy.ByGeneration)]
    [InlineData(ReconcileStrategy.ByResourceVersion)]
    public void ReconcileStrategy_Should_Be_Settable(ReconcileStrategy strategy)
    {
        var settings = new OperatorSettings { ReconcileStrategy = strategy };
        settings.ReconcileStrategy.Should().Be(strategy);
    }
}
