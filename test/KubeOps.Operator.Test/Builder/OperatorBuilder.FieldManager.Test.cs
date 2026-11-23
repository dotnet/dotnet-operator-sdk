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

using Moq;

namespace KubeOps.Operator.Test.Builder;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Configuration)]
public sealed class OperatorBuilderFieldManagerTest
{
    [Fact]
    public void Default_Kubernetes_Client_Writes_With_The_Operator_Name()
    {
        using var provider = BuildProvider(new OperatorSettingsBuilder().WithName("my-operator"));

        FieldManagerOf(provider.GetRequiredService<IKubernetes>()).Should().Be("my-operator");
    }

    [Fact]
    public void Default_Kubernetes_Client_Writes_With_The_Configured_Field_Manager()
    {
        using var provider = BuildProvider(
            new OperatorSettingsBuilder().WithName("my-operator").WithFieldManager("my-field-manager"));

        FieldManagerOf(provider.GetRequiredService<IKubernetes>()).Should().Be("my-field-manager");
    }

    [Fact]
    public void KubernetesClient_Uses_The_Registered_Kubernetes_Client()
    {
        using var provider = BuildProvider(new OperatorSettingsBuilder());

        provider.GetRequiredService<IKubernetesClient>().ApiClient
            .Should().BeSameAs(provider.GetRequiredService<IKubernetes>());
    }

    [Fact]
    public void Own_Kubernetes_Client_Is_Kept()
    {
        var ownClient = Mock.Of<IKubernetes>();

        using var provider = BuildProvider(new OperatorSettingsBuilder(), services => services.AddSingleton(ownClient));

        provider.GetRequiredService<IKubernetes>().Should().BeSameAs(ownClient);
        provider.GetRequiredService<IKubernetesClient>().ApiClient.Should().BeSameAs(ownClient);
    }

    private static ServiceProvider BuildProvider(
        OperatorSettingsBuilder settings,
        Action<IServiceCollection>? registerBeforeOperator = null)
    {
        var services = new ServiceCollection();

        // Registered before the operator so that no kubeconfig is required.
        services.AddSingleton(new KubernetesClientConfiguration { Host = "https://kubernetes.test" });
        registerBeforeOperator?.Invoke(services);
        _ = new OperatorBuilder(services, settings.Build());

        return services.BuildServiceProvider();
    }

    private static string FieldManagerOf(IKubernetes client) =>
        KubernetesHandlerChain.FirstHandlerOf(client)
            .Should().BeOfType<FieldManagerHandler>()
            .Which.FieldManager;
}
