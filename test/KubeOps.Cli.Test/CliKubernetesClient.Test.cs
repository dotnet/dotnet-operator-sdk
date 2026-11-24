// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;

using KubeOps.KubernetesClient;
using KubeOps.TestHelpers;

namespace KubeOps.Cli.Test;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Cli)]
public sealed class CliKubernetesClientTest
{
    [Fact]
    public void Create_Returns_A_Client_That_Writes_As_KubeOps_Cli()
    {
        using var client = CliKubernetesClient.Create(
            new KubernetesClientConfiguration { Host = "https://kubernetes.test" });

        KubernetesHandlerChain.FirstHandlerOf(client)
            .Should().BeOfType<FieldManagerHandler>()
            .Which.FieldManager.Should().Be("kubeops-cli");
    }

    [Fact]
    public void Create_Returns_A_Client_That_Identifies_Itself_As_KubeOps_Cli()
    {
        using var client = CliKubernetesClient.Create(
            new KubernetesClientConfiguration { Host = "https://kubernetes.test" });

        client.Should().BeOfType<Kubernetes>().Which.HttpClient.DefaultRequestHeaders.UserAgent.ToString()
            .Should().StartWith("kubeops-cli/").And.EndWith(KubernetesUserAgent.ClientDefault);
    }
}
