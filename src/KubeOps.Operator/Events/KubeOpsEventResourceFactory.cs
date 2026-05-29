// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Events;

using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Events;

/// <summary>
/// Default implementation of <see cref="IEventResourceFactory"/> that constructs
/// <see cref="Corev1Event"/> instances using the operator settings.
/// </summary>
public class KubeOpsEventResourceFactory(OperatorSettings settings, ILogger<EventPublisher>? logger = null)
    : IEventResourceFactory
{
    /// <inheritdoc />
    public virtual Corev1Event CreateEvent(
        IKubernetesObject<V1ObjectMeta> entity,
        string reason,
        string message,
        EventType type)
    {
        var @namespace = entity.Namespace() ?? "default";
        var eventName = $"{entity.Uid()}.{entity.Name()}.{@namespace}.{reason}.{message}.{type}";

        if (logger?.IsEnabled(LogLevel.Trace) == true)
        {
            logger?.LogTrace(
                "Encoding event name with: {ResourceName}.{ResourceNamespace}.{Reason}.{Message}.{Type}.",
                entity.Name(),
                @namespace,
                reason,
                message,
                type);
        }

        var encodedEventName = EventNameEncoder.Encode(eventName);

        return new Corev1Event
        {
            Metadata = new()
            {
                Name = encodedEventName,
                NamespaceProperty = @namespace,
                Annotations =
                    new Dictionary<string, string>
                    {
                        { "originalName", eventName },
                        { "nameHash", "sha512" },
                        { "nameEncoding", "Hex String" },
                    },
            },
            Type = type.ToString(),
            Reason = reason,
            Message = message,
            ReportingComponent = settings.Name,
            ReportingInstance = Environment.MachineName,
            Source = new() { Component = settings.Name, },
            InvolvedObject = entity.MakeObjectReference(),
        }.Initialize();
    }
}
