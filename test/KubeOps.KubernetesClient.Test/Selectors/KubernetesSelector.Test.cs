// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.KubernetesClient.Selectors;

namespace KubeOps.KubernetesClient.Test;

[Trait("Area", "Selectors")]
public sealed class KubernetesSelectorTest
{
    [Fact]
    public void Should_Split_Label_And_Field_Selectors()
    {
        KubernetesSelector[] selectors =
        [
            new EqualsLabelSelector("app", "demo"),
            new EqualsFieldSelector("metadata.name", "demo"),
        ];

        var (labelSelector, fieldSelector) = selectors.ToExpressions();

        labelSelector.Should().Be("app in (demo)");
        fieldSelector.Should().Be("metadata.name=demo");
    }

    [Fact]
    public void Should_Preserve_Order_Within_Each_Selector_Type()
    {
        KubernetesSelector[] selectors =
        [
            new EqualsFieldSelector("metadata.name", "demo"),
            new EqualsLabelSelector("app", "demo"),
            new NotEqualsFieldSelector("metadata.namespace", "kube-system"),
            new NotEqualsLabelSelector("tier", "frontend"),
        ];

        var (labelSelector, fieldSelector) = selectors.ToExpressions();

        labelSelector.Should().Be("app in (demo),tier notin (frontend)");
        fieldSelector.Should().Be("metadata.name=demo,metadata.namespace!=kube-system");
    }

    [Fact]
    public void Should_Return_Empty_Expressions_When_No_Selectors_Are_Provided()
    {
        var (labelSelector, fieldSelector) = Array.Empty<KubernetesSelector>().ToExpressions();

        labelSelector.Should().BeEmpty();
        fieldSelector.Should().BeEmpty();
    }
}
