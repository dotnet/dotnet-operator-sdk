// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s;

namespace KubeOps.TestHelpers;

/// <summary>Reads the HTTP handler chain of a <see cref="Kubernetes"/> client.</summary>
internal static class KubernetesHandlerChain
{
    // Kubernetes exposes the start of its handler chain only to derived classes.
    private static readonly PropertyInfo FirstMessageHandler = typeof(Kubernetes).GetProperty(
        nameof(FirstMessageHandler),
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    /// <summary>Returns the outermost handler that every request of <paramref name="client"/> passes.</summary>
    public static object? FirstHandlerOf(IKubernetes client) => FirstMessageHandler.GetValue(client);
}
