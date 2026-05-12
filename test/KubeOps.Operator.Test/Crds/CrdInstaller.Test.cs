// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Crds;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.DependencyInjection;
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

        var installer = CreateInstaller(
            clientMock.Object,
            CreateCrdResourceFactoryMock().Object);

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
            .Returns<string, string?, CancellationToken>((_, _, _) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new HttpRequestException("API server unavailable");
                }

                secondAttempt.Set();
                return Task.FromResult<V1CustomResourceDefinition?>(null);
            });

        var installer = CreateInstaller(
            clientMock.Object,
            CreateCrdResourceFactoryMock().Object);

        await installer.StartAsync(TestContext.Current.CancellationToken);

        SpinWait.SpinUntil(
            () => secondAttempt.IsSet,
            TimeSpan.FromSeconds(1)).Should().BeTrue();
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

        var installer = CreateInstaller(
            clientMock.Object,
            CreateCrdResourceFactoryMock().Object);

        await installer.StartAsync(TestContext.Current.CancellationToken);

        SpinWait.SpinUntil(
            () => firstAttempt.IsSet,
            TimeSpan.FromSeconds(1)).Should().BeTrue();
        await Task.Delay(
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);
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

        var installer = CreateInstaller(
            clientMock.Object,
            CreateCrdResourceFactoryMock().Object,
            _ => TimeSpan.FromSeconds(10));

        await installer.StartAsync(TestContext.Current.CancellationToken);

        SpinWait.SpinUntil(
            () => firstAttempt.IsSet,
            TimeSpan.FromSeconds(1)).Should().BeTrue();
        var stopTask = installer.StopAsync(TestContext.Current.CancellationToken);

        await stopTask.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Invoke_Custom_CrdResourceFactory_When_Registered_Before_AddKubernetesOperator()
    {
        // Arrange: client and custom factory that tracks calls.
        var clientMock = CreateClientMock();
        clientMock
            .Setup(c => c.GetAsync<V1CustomResourceDefinition>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((V1CustomResourceDefinition?)null);
        clientMock
            .Setup(c => c.CreateAsync(
                It.IsAny<V1CustomResourceDefinition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((V1CustomResourceDefinition crd, CancellationToken _) => crd);

        var crdFactoryMock = CreateCrdResourceFactoryMock();

        var services = new ServiceCollection();
        services.AddLogging();

        // Register custom factory BEFORE AddKubernetesOperator.
        services.AddSingleton(crdFactoryMock.Object);

        // Override the kubernetes client with our mock.
        services.AddSingleton(clientMock.Object);

        services.AddKubernetesOperator().AddCrdInstaller();

        var sp = services.BuildServiceProvider();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: resolve CrdInstaller and verify DI wiring.
        var resolvedFactory = sp.GetRequiredService<ICrdResourceFactory>();
        resolvedFactory.Should().BeSameAs(
            crdFactoryMock.Object,
            "the custom factory registered before AddKubernetesOperator should win over the default");

        // Resolve and invoke CrdInstaller directly to verify it uses the custom factory.
        var installer = ActivatorUtilities.GetServiceOrCreateInstance<OperatorCrdInstaller>(sp);

        await installer.StartAsync(cancellationToken);
        await installer.WaitForInstallCompletedAsync(cancellationToken);

        // Assert: the custom factory should have been invoked for entities in the entry assembly.
        crdFactoryMock.Verify(
            f => f.CreateCustomResourceDefinitions(It.IsAny<IReadOnlyCollection<Type>>()),
            Times.AtLeastOnce());
    }

    private static OperatorCrdInstaller CreateInstaller(
        IKubernetesClient client,
        ICrdResourceFactory crdResourceFactory,
        Func<uint, TimeSpan>? retryDelayFactory = null)
        => new(
            Mock.Of<ILogger<OperatorCrdInstaller>>(),
            new CrdInstallerSettingsBuilder().Build(),
            client,
            crdResourceFactory,
            retryDelayFactory ?? (_ => TimeSpan.Zero));

    private static Mock<IKubernetesClient> CreateClientMock()
    {
        var clientMock = new Mock<IKubernetesClient>();

        clientMock
            .Setup(c => c.CreateAsync(
                It.IsAny<V1CustomResourceDefinition>(),
                It.IsAny<CancellationToken>()))
            .Returns<V1CustomResourceDefinition, CancellationToken>((crd, _) => Task.FromResult(crd));

        return clientMock;
    }

    private static Mock<ICrdResourceFactory> CreateCrdResourceFactoryMock()
    {
        var factoryMock = new Mock<ICrdResourceFactory>();

        factoryMock
            .Setup(f => f.CreateCustomResourceDefinitions(It.IsAny<IReadOnlyCollection<Type>>()))
            .Returns((IReadOnlyCollection<Type> types) => types.Select(t => new V1CustomResourceDefinition
            {
                ApiVersion = "apiextensions.k8s.io/v1",
                Kind = "CustomResourceDefinition",
                Metadata = new V1ObjectMeta { Name = $"{t.Name.ToLower()}.test" },
                Spec = new V1CustomResourceDefinitionSpec
                {
                    Group = "test",
                    Names = new V1CustomResourceDefinitionNames { Kind = t.Name, Plural = t.Name.ToLower() },
                    Scope = "Namespaced",
                    Versions =
                    [
                        new V1CustomResourceDefinitionVersion { Name = "v1", Served = true, Storage = true }
                    ],
                },
            }).ToList());

        return factoryMock;
    }
}
