// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Events;

/// <summary>
/// Represents a type used to create <see cref="EventPublisher"/>s for clients and controllers.
/// </summary>
public interface IEventPublisherFactory
{
    /// <summary>
    /// Creates a new event publisher.
    /// </summary>
    /// <returns>The <see cref="EventPublisher"/>.</returns>
    EventPublisher Create();
}
