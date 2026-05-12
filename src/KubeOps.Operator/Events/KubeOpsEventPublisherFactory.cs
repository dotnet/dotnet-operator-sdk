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
    ILogger<EventPublisher> logger) : IEventPublisherFactory
{
    public EventPublisher Create() => async (entity, reason, message, type, token) =>
    {
        var @event = eventResourceFactory.CreateEvent(entity, reason, message, type);

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
            await client.SaveAsync(@event, token);

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
