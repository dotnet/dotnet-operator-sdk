// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Crds;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Logging;

using Moq;

using OperatorCrdInstaller = KubeOps.Operator.Crds.CrdInstaller;

namespace KubeOps.Operator.Test.Crds;

public sealed class CrdInstallerTest
{
    [Fact]
    public async Task StartAsync_Should_Not_Propagate_Transient_Error()
    {
        var clientMock = CreateClientMock();
        clientMock
            .Setup(c => c.GetAsync<V1CustomResourceDefinition>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API server unavailable"));

        var installer = CreateInstaller(clientMock.Object);

        Func<Task> action = () => installer.StartAsync(TestContext.Current.CancellationToken);

        await action.Should().NotThrowAsync();
        await installer.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Transient_Error_Should_Be_Retried()
    {
        var clientMock = CreateClientMock();
        using var secondAttempt = new ManualResetEventSlim(false);
        var attempts = 0;

        clientMock
            .Setup(c => c.GetAsync<V1CustomResourceDefinition>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string?, CancellationToken>(
                (_, _, _) =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        throw new HttpRequestException("API server unavailable");
                    }

                    secondAttempt.Set();
                    return Task.FromResult<V1CustomResourceDefinition?>(null);
                });

        var installer = CreateInstaller(clientMock.Object);

        await installer.StartAsync(TestContext.Current.CancellationToken);

        SpinWait.SpinUntil(() => secondAttempt.IsSet, TimeSpan.FromSeconds(1)).Should().BeTrue();
        await installer.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Non_Transient_Kubernetes_Error_Should_Not_Be_Retried()
    {
        var clientMock = CreateClientMock();
        using var firstAttempt = new ManualResetEventSlim(false);

        clientMock
            .Setup(c => c.GetAsync<V1CustomResourceDefinition>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => firstAttempt.Set())
            .ThrowsAsync(new KubernetesException(new V1Status { Code = (int)HttpStatusCode.Forbidden }));

        var installer = CreateInstaller(clientMock.Object);

        await installer.StartAsync(TestContext.Current.CancellationToken);

        SpinWait.SpinUntil(() => firstAttempt.IsSet, TimeSpan.FromSeconds(1)).Should().BeTrue();
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        await installer.StopAsync(TestContext.Current.CancellationToken);

        clientMock.Verify(
            c => c.GetAsync<V1CustomResourceDefinition>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StopAsync_Should_Cancel_Pending_Retry_Delay()
    {
        var clientMock = CreateClientMock();
        using var firstAttempt = new ManualResetEventSlim(false);

        clientMock
            .Setup(c => c.GetAsync<V1CustomResourceDefinition>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => firstAttempt.Set())
            .ThrowsAsync(new HttpRequestException("API server unavailable"));

        var installer = CreateInstaller(clientMock.Object, _ => TimeSpan.FromSeconds(10));

        await installer.StartAsync(TestContext.Current.CancellationToken);

        SpinWait.SpinUntil(() => firstAttempt.IsSet, TimeSpan.FromSeconds(1)).Should().BeTrue();
        var stopTask = installer.StopAsync(TestContext.Current.CancellationToken);

        await stopTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    private static OperatorCrdInstaller CreateInstaller(
        IKubernetesClient client,
        Func<uint, TimeSpan>? retryDelayFactory = null)
        => new(
            Mock.Of<ILogger<OperatorCrdInstaller>>(),
            new CrdInstallerSettingsBuilder().Build(),
            client,
            retryDelayFactory ?? (_ => TimeSpan.Zero));

    private static Mock<IKubernetesClient> CreateClientMock()
    {
        var clientMock = new Mock<IKubernetesClient>();
        clientMock
            .Setup(c => c.CreateAsync(It.IsAny<V1CustomResourceDefinition>(), It.IsAny<CancellationToken>()))
            .Returns<V1CustomResourceDefinition, CancellationToken>((crd, _) => Task.FromResult(crd));

        return clientMock;
    }
}
