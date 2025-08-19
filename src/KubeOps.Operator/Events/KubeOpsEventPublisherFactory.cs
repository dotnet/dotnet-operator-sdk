// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Events;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Events;

internal sealed class KubeOpsEventPublisherFactory(
    IKubernetesClient client,
    OperatorSettings settings,
    ILogger<EventPublisher> logger) : IEventPublisherFactory
{
    public EventPublisher Create() => async (entity, reason, message, type, token) =>
    {
        var @namespace = entity.Namespace() ?? "default";
        logger.LogTrace(
            "Encoding event name with: {ResourceName}.{ResourceNamespace}.{Reason}.{Message}.{Type}.",
            entity.Name(),
            @namespace,
            reason,
            message,
            type);

        var eventName = $"{entity.Uid()}.{entity.Name()}.{@namespace}.{reason}.{message}.{type}";
        var encodedEventName =
            Convert.ToHexString(
                SHA512.HashData(
                    Encoding.UTF8.GetBytes(eventName)));

        logger.LogTrace("""Search or create event with name "{Name}".""", encodedEventName);

        Corev1Event? @event;
        try
        {
            @event = await client.GetAsync<Corev1Event>(encodedEventName, @namespace, token);
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                """Could not receive event with name "{EventName}" on entity "{Kind}/{Name}".""",
                eventName,
                entity.Kind,
                entity.Name());
            return;
        }

        @event ??= new Corev1Event
        {
            Metadata = new()
            {
                Name = encodedEventName,
                NamespaceProperty = @namespace,
                Annotations =
                    new Dictionary<string, string>
                    {
                        { "originalName", eventName }, { "nameHash", "sha512" }, { "nameEncoding", "Hex String" },
                    },
            },
            Type = type.ToString(),
            Reason = reason,
            Message = message,
            ReportingComponent = settings.Name,
            ReportingInstance = Environment.MachineName,
            Source = new() { Component = settings.Name, },
            InvolvedObject = entity.MakeObjectReference(),
            FirstTimestamp = DateTime.UtcNow,
            LastTimestamp = DateTime.UtcNow,
            Count = 0,
        }.Initialize();

        @event.Count++;
        @event.LastTimestamp = DateTime.UtcNow;
        logger.LogTrace(
            "Save event with new count {Count} and last timestamp {Timestamp}",
            @event.Count,
            @event.LastTimestamp);

        try
        {
            await client.SaveAsync(@event, token);
            logger.LogInformation(
                """Created or updated event with name "{EventName}" to new count {Count} on entity "{Kind}/{Name}".""",
                eventName,
                @event.Count,
                entity.Kind,
                entity.Name());
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                """Could not publish event with name "{EventName}" on entity "{Kind}/{Name}".""",
                eventName,
                entity.Kind,
                entity.Name());
        }
    };
}
