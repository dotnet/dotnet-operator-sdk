// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Operator.Watcher;

namespace KubeOps.Operator.Test.Watcher;

public sealed class LabelSelectorMatcherTest
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("app=nginx", true)]
    [InlineData("app==nginx", true)]
    [InlineData("app=apache", false)]
    [InlineData("app!=apache", true)]
    [InlineData("app!=nginx", false)]
    [InlineData("missing!=anything", true)]
    [InlineData("app", true)]
    [InlineData("missing", false)]
    [InlineData("!missing", true)]
    [InlineData("!app", false)]
    [InlineData("app in (nginx, apache)", true)]
    [InlineData("app in (apache, haproxy)", false)]
    [InlineData("missing in (nginx)", false)]
    [InlineData("app notin (apache)", true)]
    [InlineData("app notin (nginx)", false)]
    [InlineData("missing notin (nginx)", true)]
    [InlineData("app=nginx,tier=frontend", true)]
    [InlineData("app=nginx,tier=backend", false)]
    [InlineData("app in (nginx, apache),tier=frontend,!missing", true)]
    [InlineData("app in (nginx, apache),tier=backend", false)]
    public void Should_Match_Standard_Selector_Syntax(string? selector, bool expected)
    {
        var labels = new Dictionary<string, string>
        {
            ["app"] = "nginx",
            ["tier"] = "frontend",
        };

        var requirements = LabelSelectorMatcher.Parse(selector);

        LabelSelectorMatcher.Matches(requirements, labels).Should().Be(expected);
    }

    [Fact]
    public void Should_Treat_Null_Labels_As_No_Labels()
    {
        LabelSelectorMatcher.Matches(LabelSelectorMatcher.Parse("app=nginx"), null).Should().BeFalse();
        LabelSelectorMatcher.Matches(LabelSelectorMatcher.Parse("!app"), null).Should().BeTrue();
        LabelSelectorMatcher.Matches(LabelSelectorMatcher.Parse("app!=nginx"), null).Should().BeTrue();
        LabelSelectorMatcher.Matches(LabelSelectorMatcher.Parse(null), null).Should().BeTrue();
    }

    [Theory]
    [InlineData("app in nginx")]
    [InlineData("app in (nginx")]
    [InlineData("app in nginx)")]
    [InlineData("app in ()")]
    [InlineData("app=nginx,,tier=frontend")]
    public void Should_Throw_For_Invalid_Selector(string selector)
    {
        var act = () => LabelSelectorMatcher.Parse(selector);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Should_Not_Split_On_Commas_Inside_Value_Lists()
    {
        var labels = new Dictionary<string, string> { ["env"] = "staging" };

        var requirements = LabelSelectorMatcher.Parse("env in (dev, staging, prod)");

        requirements.Should().ContainSingle();
        LabelSelectorMatcher.Matches(requirements, labels).Should().BeTrue();
    }
}
