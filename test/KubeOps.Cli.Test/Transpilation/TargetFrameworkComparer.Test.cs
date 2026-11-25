// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Cli.Transpilation;

namespace KubeOps.Cli.Test.Transpilation;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Cli)]
public sealed class TargetFrameworkComparerTest
{
    [Theory]
    [InlineData("netstandard2.1", "netcoreapp2.1")]
    [InlineData("netcoreapp5.0", "net5.0")]
    [InlineData("netstandard2.1", "net2.1")]
    [InlineData("net8.0", "net10.0")]
    [InlineData("net10.0", "net10.1")]
    public void Compare_Orders_The_Older_Target_Framework_First(string older, string newer)
    {
        var comparer = new TargetFrameworkComparer();

        comparer.Compare(older, newer).Should().BeNegative();
        comparer.Compare(newer, older).Should().BePositive();
    }

    [Fact]
    public void Compare_Treats_Equal_Target_Frameworks_As_Equal()
    {
        new TargetFrameworkComparer().Compare("net10.0", "net10.0").Should().Be(0);
    }
}
