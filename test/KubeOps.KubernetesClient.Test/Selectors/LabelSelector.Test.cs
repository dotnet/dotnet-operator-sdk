// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.KubernetesClient.Selectors;

namespace KubeOps.KubernetesClient.Test;

public sealed class LabelSelectorTest : IntegrationTestBase
{
    [Fact]
    public void Should_Return_Correct_Expression()
    {
        var labelSelectors = new LabelSelector[] {
            new EqualsLabelSelector("app", Enumerable.Range(0,3).Select(x=>$"app-{x}").ToArray()),
            new NotEqualsLabelSelector("srv", Enumerable.Range(0,2).Select(x=>$"service-{x}").ToArray())
        };

        const string expected = "app in (app-0,app-1,app-2),srv notin (service-0,service-1)";
        var actual = labelSelectors.ToExpression();
        Assert.Equal(expected, actual);
    }
}
