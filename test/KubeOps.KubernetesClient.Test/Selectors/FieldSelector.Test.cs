// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.KubernetesClient.Selectors;

namespace KubeOps.KubernetesClient.Test;

public sealed class FieldSelectorTest : IntegrationTestBase
{
    [Fact]
    public void Should_Return_Correct_Expression_For_Equals()
    {
        var selector = new EqualsFieldSelector("metadata.name", "my-resource");
        string actual = selector;
        actual.Should().Be("metadata.name=my-resource");
    }

    [Fact]
    public void Should_Return_Correct_Expression_For_NotEquals()
    {
        var selector = new NotEqualsFieldSelector("metadata.namespace", "kube-system");
        string actual = selector;
        actual.Should().Be("metadata.namespace!=kube-system");
    }

    [Fact]
    public void Should_Return_Correct_Combined_Expression()
    {
        var fieldSelectors = new FieldSelector[]
        {
            new EqualsFieldSelector("metadata.name", "my-resource"),
            new NotEqualsFieldSelector("metadata.namespace", "kube-system"),
        };

        const string expected = "metadata.name=my-resource,metadata.namespace!=kube-system";
        var actual = fieldSelectors.ToExpression();
        actual.Should().Be(expected);
    }
}
