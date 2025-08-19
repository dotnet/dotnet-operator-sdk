// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

namespace KubeOps.Abstractions.Events;

/// <summary>
/// The type of a <see cref="Corev1Event"/>.
/// The event type will be stringified and used as <see cref="Corev1Event.Type"/>.
/// </summary>
public enum EventType
{
    /// <summary>
    /// A normal event, informative value.
    /// </summary>
    Normal,

    /// <summary>
    /// A warning, something might went wrong.
    /// </summary>
    Warning,
}
