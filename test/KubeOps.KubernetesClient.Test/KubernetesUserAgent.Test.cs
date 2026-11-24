// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using FluentAssertions;

using k8s;

using KubeOps.TestHelpers;

namespace KubeOps.KubernetesClient.Test;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.KubernetesClient)]
public sealed class KubernetesUserAgentTest
{
    private static readonly string ClientDefault = new KubernetesClientConfiguration().UserAgent;

    private static readonly string KubeOpsVersion = typeof(KubernetesUserAgent).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    [Fact]
    public void Create_Starts_With_Product_And_Version_Followed_By_KubeOps_And_The_Client()
    {
        ClientDefault.Should().StartWith("kubernetes-csharp/");

        KubernetesUserAgent.Create("my-operator", "1.2.0")
            .Should().Be($"my-operator/1.2.0 kubeops/{KubeOpsVersion} {ClientDefault}");
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

    [Fact]
    public void Create_Drops_Build_Metadata_Of_The_Version()
    {
        KubernetesUserAgent.Create("my-operator", "1.2.0+3f7accac")
            .Should().StartWith("my-operator/1.2.0 kubeops/");
    }

    [Theory]
    [InlineData("my operator", "my-operator")]
    [InlineData("team/operator", "team-operator")]
    [InlineData(" my-operator ", "my-operator")]
    [InlineData("Operator_1.0~test", "Operator_1.0~test")]
    [InlineData("opérateur", "op-rateur")]
    [InlineData("my\r\noperator", "my--operator")]
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    public void Apply_Sets_The_User_Agent_Unless_One_Is_Configured(string? configured)
    {
        var config = new KubernetesClientConfiguration
        {
            UserAgent = configured == "default" ? ClientDefault : configured,
        };

        KubernetesUserAgent.Apply(config, "my-operator", "1.2.0");

        config.UserAgent.Should().Be(KubernetesUserAgent.Create("my-operator", "1.2.0"));
    }

    [Fact]
    public void Apply_Keeps_An_Own_User_Agent()
    {
        var config = new KubernetesClientConfiguration { UserAgent = "own-agent/1.0" };

        KubernetesUserAgent.Apply(config, "my-operator", "1.2.0");

        config.UserAgent.Should().Be("own-agent/1.0");
    }
}
