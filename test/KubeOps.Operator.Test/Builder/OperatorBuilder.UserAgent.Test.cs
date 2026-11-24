// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;

using KubeOps.Abstractions.Builder;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Builder;
using KubeOps.TestHelpers;

using Microsoft.Extensions.DependencyInjection;

namespace KubeOps.Operator.Test.Builder;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Configuration)]
public sealed class OperatorBuilderUserAgentTest
{
    [Fact]
    public void Default_Kubernetes_Client_Identifies_Itself_With_The_Operator_Name()
    {
        using var provider = BuildProvider(
            new OperatorSettingsBuilder().WithName("my-operator"),
            new KubernetesClientConfiguration { Host = "https://kubernetes.test" });

        var userAgent = UserAgentOf(provider.GetRequiredService<IKubernetes>());

        userAgent.Should().StartWith("my-operator").And.Contain(" kubeops/")
            .And.EndWith(KubernetesUserAgent.ClientDefault);
    }

    [Fact]
    public void Default_Kubernetes_Client_Keeps_An_Own_User_Agent_Of_The_Configuration()
    {
        using var provider = BuildProvider(
            new OperatorSettingsBuilder().WithName("my-operator"),
            new KubernetesClientConfiguration { Host = "https://kubernetes.test", UserAgent = "own-agent/1.0" });

        UserAgentOf(provider.GetRequiredService<IKubernetes>()).Should().Be("own-agent/1.0");
    }

    [Fact]
    public void User_Agent_Is_Independent_Of_The_Field_Manager()
    {
        using var provider = BuildProvider(
            new OperatorSettingsBuilder().WithName("my-operator").WithFieldManager("my-field-manager"),
            new KubernetesClientConfiguration { Host = "https://kubernetes.test" });

        UserAgentOf(provider.GetRequiredService<IKubernetes>()).Should().StartWith("my-operator");
    }

    private static ServiceProvider BuildProvider(
        OperatorSettingsBuilder settings,
        KubernetesClientConfiguration config)
    {
        var services = new ServiceCollection();

        // Registered before the operator so that no kubeconfig is required.
        services.AddSingleton(config);
        _ = new OperatorBuilder(services, settings.Build());

        return services.BuildServiceProvider();
    }

    private static string UserAgentOf(IKubernetes client) =>
        client.Should().BeOfType<Kubernetes>().Which.HttpClient.DefaultRequestHeaders.UserAgent.ToString();
}
