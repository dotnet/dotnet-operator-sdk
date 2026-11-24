// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Reflection;
using System.Text;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.TestHelpers;

namespace KubeOps.KubernetesClient.Test;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class KubernetesUserAgentTest
{
    private static readonly string KubeOpsVersion = typeof(KubernetesUserAgent).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    [Fact]
    public void Client_Default_Is_The_User_Agent_Of_The_CSharp_Client()
    {
        KubernetesUserAgent.ClientDefault.Should().StartWith("kubernetes-csharp/");
    }

    [Fact]
    public void Create_Starts_With_Product_And_Version_Followed_By_KubeOps_And_The_Client()
    {
        KubernetesUserAgent.Create("my-operator", "1.2.0")
            .Should().Be($"my-operator/1.2.0 kubeops/{KubeOpsVersion} {KubernetesUserAgent.ClientDefault}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_Without_Version_Omits_It(string? version)
    {
        KubernetesUserAgent.Create("my-operator", version)
            .Should().StartWith("my-operator kubeops/");
    }

    [Theory]
    [InlineData("my operator", "my-operator")]
    [InlineData("team/operator", "team-operator")]
    [InlineData(" my-operator ", "my-operator")]
    [InlineData("Operator_1.0~test", "Operator_1.0~test")]
    [InlineData("opérateur", "op-rateur")]
    public void Create_Replaces_Characters_That_Are_Not_Allowed_In_A_Product_Token(string product, string token)
    {
        KubernetesUserAgent.Create(product).Should().StartWith($"{token} kubeops/");
    }

    [Fact]
    public void Create_Replaces_Characters_That_Are_Not_Allowed_In_The_Version()
    {
        KubernetesUserAgent.Create("my-operator", "1.2.0 (beta)")
            .Should().StartWith("my-operator/1.2.0--beta- kubeops/");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_Rejects_An_Empty_Product(string product)
    {
        var create = () => KubernetesUserAgent.Create(product);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_From_Assembly_Uses_Its_Informational_Version()
    {
        KubernetesUserAgent.CreateFromAssembly("kubeops", typeof(KubernetesUserAgent).Assembly)
            .Should().StartWith($"kubeops/{KubeOpsVersion} kubeops/");
    }

    [Fact]
    public void Create_From_Assembly_Without_Assembly_Omits_The_Version()
    {
        KubernetesUserAgent.CreateFromAssembly("my-operator", null)
            .Should().StartWith("my-operator kubeops/");
    }

    [Fact]
    public void Set_If_Not_Configured_Replaces_The_Client_Default()
    {
        var config = new KubernetesClientConfiguration();

        KubernetesUserAgent.SetIfNotConfigured(config, "my-operator/1.2.0").Should().BeTrue();

        config.UserAgent.Should().Be("my-operator/1.2.0");
    }

    [Fact]
    public void Set_If_Not_Configured_Replaces_A_Missing_User_Agent()
    {
        var config = new KubernetesClientConfiguration { UserAgent = null };

        KubernetesUserAgent.SetIfNotConfigured(config, "my-operator/1.2.0").Should().BeTrue();

        config.UserAgent.Should().Be("my-operator/1.2.0");
    }

    [Fact]
    public void Set_If_Not_Configured_Keeps_An_Own_User_Agent()
    {
        var config = new KubernetesClientConfiguration { UserAgent = "own-agent/1.0" };

        KubernetesUserAgent.SetIfNotConfigured(config, "my-operator/1.2.0").Should().BeFalse();

        config.UserAgent.Should().Be("own-agent/1.0");
    }

    [Fact]
    public async Task Client_Sends_The_Configured_User_Agent()
    {
        var config = new KubernetesClientConfiguration { Host = "https://kubernetes.test" };
        KubernetesUserAgent.SetIfNotConfigured(config, KubernetesUserAgent.Create("my-operator", "1.2.0"));
        var apiServer = new UserAgentRecorder();
        using var client = new Kubernetes(config, apiServer);

        await client.CoreV1.ReadNamespacedConfigMapAsync(
            "my-config",
            "default",
            cancellationToken: TestContext.Current.CancellationToken);

        apiServer.UserAgent.Should().Be(KubernetesUserAgent.Create("my-operator", "1.2.0"));
    }

    private sealed class UserAgentRecorder : DelegatingHandler
    {
        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            UserAgent = request.Headers.UserAgent.ToString();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(
                    KubernetesJson.Serialize(new V1ConfigMap { Metadata = new() { Name = "my-config" } }),
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
