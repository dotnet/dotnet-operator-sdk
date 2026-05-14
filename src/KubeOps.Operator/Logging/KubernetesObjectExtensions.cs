// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Operator.Logging;

/// <summary>
/// Provides extension methods for <see cref="IKubernetesObject{V1ObjectMeta}"/> to support logging and diagnostics.
/// </summary>
public static class KubernetesObjectExtensions
{
    /// <summary>
    /// Builds a human-readable identifier string for the Kubernetes object, suitable for use in log messages.
    /// </summary>
    /// <param name="kubernetesObject">The Kubernetes object to identify.</param>
    /// <returns>
    /// A string that identifies the object. The format varies depending on which metadata fields are populated:
    /// <list type="bullet">
    /// <item><description><c>Kind/Name (UID: uid)</c> when all fields are present.</description></item>
    /// <item><description><c>Kind (UID: uid)</c> when the name is absent.</description></item>
    /// <item><description><c>Kind/Name</c> when the UID is absent.</description></item>
    /// <item><description><c>Kind</c> when only the kind is present.</description></item>
    /// </list>
    /// </returns>
    /// <example>
    /// <code language="csharp">
    /// IKubernetesObject&lt;V1ObjectMeta&gt; entity = ...;
    /// logger.LogInformation("Processing {Identifier}.", entity.ToIdentifierString());
    /// // Output: "Processing MyKind/my-name (UID: 1a2b3c)."
    /// </code>
    /// </example>
    public static string ToIdentifierString(this IKubernetesObject<V1ObjectMeta> kubernetesObject)
        => $"{kubernetesObject.Kind}{(string.IsNullOrEmpty(kubernetesObject.Name()) ? string.Empty : $"/{kubernetesObject.Name()}")}{(string.IsNullOrEmpty(kubernetesObject.Uid()) ? string.Empty : $" (UID: {kubernetesObject.Uid()})")}";

    /// <summary>
    /// Builds a human-readable identifier string for the Kubernetes object reference, suitable for use in log messages.
    /// </summary>
    /// <param name="objectReference">The Kubernetes object reference to identify.</param>
    /// <returns>
    /// A string that identifies the object reference. The format varies depending on which fields are populated:
    /// <list type="bullet">
    /// <item><description><c>Kind/Name (UID: uid)</c> when all fields are present.</description></item>
    /// <item><description><c>Kind (UID: uid)</c> when the name is absent.</description></item>
    /// <item><description><c>Kind/Name</c> when the UID is absent.</description></item>
    /// <item><description><c>Kind</c> when only the kind is present.</description></item>
    /// </list>
    /// </returns>
    /// <example>
    /// <code language="csharp">
    /// V1ObjectReference objectReference = ...;
    /// logger.LogInformation("Processing {Identifier}.", objectReference.ToIdentifierString());
    /// // Output: "Processing MyKind/my-name (UID: 1a2b3c)."
    /// </code>
    /// </example>
    public static string ToIdentifierString(this V1ObjectReference objectReference)
        => $"{objectReference.Kind}{(string.IsNullOrEmpty(objectReference.Name) ? string.Empty : $"/{objectReference.Name}")}{(string.IsNullOrEmpty(objectReference.Uid) ? string.Empty : $" (UID: {objectReference.Uid})")}";

    /// <summary>
    /// Builds a human-readable identifier string for the Kubernetes owner reference, suitable for use in log messages.
    /// </summary>
    /// <param name="ownerReference">The Kubernetes owner reference to identify.</param>
    /// <returns>
    /// A string that identifies the owner reference. The format varies depending on which fields are populated:
    /// <list type="bullet">
    /// <item><description><c>Kind/Name (UID: uid)</c> when all fields are present.</description></item>
    /// <item><description><c>Kind (UID: uid)</c> when the name is absent.</description></item>
    /// <item><description><c>Kind/Name</c> when the UID is absent.</description></item>
    /// <item><description><c>Kind</c> when only the kind is present.</description></item>
    /// </list>
    /// </returns>
    /// <example>
    /// <code language="csharp">
    /// V1OwnerReference ownerReference = ...;
    /// logger.LogInformation("Processing {Identifier}.", ownerReference.ToIdentifierString());
    /// // Output: "Processing MyKind/my-name (UID: 1a2b3c)."
    /// </code>
    /// </example>
    public static string ToIdentifierString(this V1OwnerReference ownerReference)
        => $"{ownerReference.Kind}{(string.IsNullOrEmpty(ownerReference.Name) ? string.Empty : $"/{ownerReference.Name}")}{(string.IsNullOrEmpty(ownerReference.Uid) ? string.Empty : $" (UID: {ownerReference.Uid})")}";
}
