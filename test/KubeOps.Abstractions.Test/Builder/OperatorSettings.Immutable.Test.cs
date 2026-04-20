// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;

namespace KubeOps.Abstractions.Test.Builder;

public sealed class OperatorSettingsImmutableTest
{
    [Fact]
    public void Settings_Should_Throw_When_Immutable()
    {
        var settings = new OperatorSettings();
        settings.MakeImmutable();

        var act = () => { settings.Name = "changed"; };

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("OperatorSettings are immutable after the operator has been built (property: 'Name'). Configure settings via the Action<OperatorSettings> delegate in AddKubernetesOperator.");
    }

    [Fact]
    public void Settings_Should_Accept_Value_Before_MakeImmutable()
    {
        var settings = new OperatorSettings();
        settings.Name = "changed";

        settings.Name.Should().Be("changed");
    }
}
