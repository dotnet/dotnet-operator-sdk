// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Crds;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using CrdInstallerService = KubeOps.Operator.Crds.CrdInstaller;

namespace KubeOps.Operator.Test.Crds;

public sealed class CrdInstallerTest
{
    [Fact]
    public async Task Should_Invoke_Custom_CrdResourceFactory_When_Registered_Before_AddKubernetesOperator()
    {
        // Arrange: custom factory that tracks calls.
        var mockFactory = new Mock<ICrdResourceFactory>();
        mockFactory
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
                    Versions = [new V1CustomResourceDefinitionVersion { Name = "v1", Served = true, Storage = true }],
                },
            }).ToList());

        var mockClient = new Mock<IKubernetesClient>();
        mockClient
            .Setup(c => c.GetAsync<V1CustomResourceDefinition>(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((V1CustomResourceDefinition?)null);
        mockClient
            .Setup(c => c.CreateAsync(It.IsAny<V1CustomResourceDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((V1CustomResourceDefinition crd, CancellationToken _) => crd);

        var services = new ServiceCollection();
        services.AddLogging();

        // Register custom factory BEFORE AddKubernetesOperator.
        services.AddSingleton(mockFactory.Object);

        services.AddKubernetesOperator().AddCrdInstaller();

        // Override the kubernetes client with our mock.
        services.AddSingleton(mockClient.Object);

        var sp = services.BuildServiceProvider();

        // Act: resolve CrdInstaller and verify DI wiring.
        var resolvedFactory = sp.GetRequiredService<ICrdResourceFactory>();
        resolvedFactory.Should().BeSameAs(mockFactory.Object,
            "the custom factory registered before AddKubernetesOperator should win over the default");

        // Resolve and invoke CrdInstaller directly to verify it uses the custom factory.
        var installer = ActivatorUtilities.GetServiceOrCreateInstance<CrdInstallerService>(sp);

        await installer.StartAsync(CancellationToken.None);

        // Assert: the custom factory should have been invoked for entities in the entry assembly.
        mockFactory.Verify(f => f.CreateCustomResourceDefinitions(It.IsAny<IReadOnlyCollection<Type>>()), Times.AtLeastOnce());
    }
}
