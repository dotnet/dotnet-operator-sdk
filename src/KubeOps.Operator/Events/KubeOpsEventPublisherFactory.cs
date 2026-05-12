// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Events;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Logging;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Events;

internal sealed class KubeOpsEventPublisherFactory(
    IKubernetesClient client,
    IEventResourceFactory eventResourceFactory,
#pragma warning disable S6672 // Generic logger injection should match enclosing type
    // This is by design, it's not the factory but the EventPublisher that is doing the logging.
    ILogger<EventPublisher> logger) : IEventPublisherFactory
#pragma warning restore S6672 // Generic logger injection should match enclosing type
{
    public EventPublisher Create() => async (entity, reason, message, type, token) =>
    {
        var @event = eventResourceFactory.CreateEvent(entity, reason, message, type);

        if (@event.Metadata?.NamespaceProperty is null)
        {
            @event.EnsureMetadata().NamespaceProperty = "default";
        }

        logger.LogTrace("""Search or create event with name "{Name}".""", @event.Name());

        Corev1Event? existingEvent;
        try
        {
            existingEvent = await client.GetAsync<Corev1Event>(@event.Name(), @event.Namespace(), token);
        }
        catch (Exception e)
        {
            logger
                .LogError(
                    e,
                    """Could not receive event with name "{EventName}" on entity "{Identifier}".""",
                    @event.Name(),
                    entity.ToIdentifierString());
            return;
        }

        if (existingEvent is null)
        {
            @event.Count = 1;
            @event.FirstTimestamp = @event.LastTimestamp = DateTime.UtcNow;
        }
        else
        {
            @event = existingEvent;
            @event.Count++;
            @event.LastTimestamp = DateTime.UtcNow;
        }

        logger.LogTrace(
            "Save event with new count {Count} and last timestamp {Timestamp}",
            @event.Count,
            @event.LastTimestamp);

        try
        {
            if (existingEvent is null)
            {
                await client.CreateAsync(@event, token);
            }
            else
            {
                await client.UpdateAsync(@event, token);
            }

            logger
                .LogInformation(
                    """Created or updated event with name "{EventName}" to new count {Count} on entity "{Identifier}".""",
                    @event.Name(),
                    @event.Count,
                    entity.ToIdentifierString());
        }
        catch (Exception e)
        {
            logger
                .LogError(
                    e,
                    """Could not publish event with name "{EventName}" on entity "{Identifier}".""",
                    @event.Name(),
                    entity.ToIdentifierString());
        }
    };
}
