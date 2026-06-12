// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Events;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace KubeOps.Operator.Test.Events;

public sealed class EventPublisherCustomResourceFactoryTest
{
    [Fact]
    public async Task Should_Publish_Event_Created_By_Custom_EventResourceFactory()
    {
        // Arrange: a custom factory that produces events with a distinctive marker.
        var customEvent = new Corev1Event
        {
            ApiVersion = "v1",
            Kind = "Event",
            Metadata = new V1ObjectMeta { NamespaceProperty = "default", Name = "test-event" },
            Reason = "CustomReason",
            Message = "Custom message from custom factory",
            Type = "Normal",
            InvolvedObject = new V1ObjectReference { Name = "test-entity", NamespaceProperty = "default" },
            ReportingComponent = "custom-factory",
        };

        var mockFactory = new Mock<IEventResourceFactory>();

        mockFactory
            .Setup(f => f.CreateEvent(
                It.IsAny<IKubernetesObject<V1ObjectMeta>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<EventType>()))
            .Returns(customEvent);

        Corev1Event? savedEvent = null;
        var mockClient = new Mock<IKubernetesClient> { CallBase = true };

        mockClient
            .Setup(c => c.GetAsync<Corev1Event>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Corev1Event?)null);

        mockClient
            .Setup(c => c.CreateAsync(
                It.IsAny<Corev1Event>(),
                It.IsAny<CancellationToken>()))
            .Callback<Corev1Event, CancellationToken>((e, _) => savedEvent = e)
            .ReturnsAsync((Corev1Event e, CancellationToken _) => e);

        var services = new ServiceCollection();

        // Register custom factory and mock client BEFORE AddKubernetesOperator.
        services.AddSingleton(mockFactory.Object);
        services.AddSingleton(mockClient.Object);
        services.AddSingleton(new Mock<IKubernetes>().Object);
        services.AddLogging();

        services.AddKubernetesOperator();

        var sp = services.BuildServiceProvider();

        // Act: resolve EventPublisher the same way application code does and invoke it.
        var publisher = sp.GetRequiredService<EventPublisher>();

        var entity = new V1OperatorIntegrationTestEntity(
            "test-entity",
            "user",
            "default")
        { Metadata = { Uid = "test-uid" }, };

        await publisher(
            entity,
            "OriginalReason",
            "Original message",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the event saved to the client is the one produced by the custom factory.
        savedEvent.Should().NotBeNull();
        savedEvent!.ReportingComponent.Should().Be("custom-factory");
        savedEvent.Reason.Should().Be("CustomReason");
        savedEvent.Message.Should().Be("Custom message from custom factory");
    }
}
