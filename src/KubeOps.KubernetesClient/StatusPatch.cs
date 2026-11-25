// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.KubernetesClient;

/// <summary>
/// Builds and sends patches of the <c>/status</c> subresource of an entity.
/// </summary>
internal static class StatusPatch
{
    private const string Status = "status";

    /// <summary>
    /// Creates a merge patch that turns the status of <paramref name="from"/> into the status of
    /// <paramref name="to"/>. It carries only the changed fields: removed fields are set to <c>null</c>,
    /// and changed arrays are sent as a whole.
    /// </summary>
    /// <param name="from">The JSON of the entity with the original status.</param>
    /// <param name="to">The JSON of the entity with the desired status.</param>
    /// <returns>The merge patch, or <see langword="null"/> if the status did not change.</returns>
    public static V1Patch? CreateMergePatch(JsonNode? from, JsonNode? to) =>
        TryCreateDiff(from?[Status], to?[Status], out var status)
            ? new V1Patch(new JsonObject { [Status] = status }.ToJsonString(), V1Patch.PatchType.MergePatch)
            : null;

    /// <summary>
    /// Sends <paramref name="patch"/> to the <c>/status</c> subresource of the entity.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity.</typeparam>
    /// <param name="apiClient">The client to send the patch with.</param>
    /// <param name="metadata">The metadata of <typeparamref name="TEntity"/>.</param>
    /// <param name="patch">The patch.</param>
    /// <param name="name">The name of the entity.</param>
    /// <param name="namespace">The namespace of the entity, or <see langword="null"/> if cluster scoped.</param>
    /// <param name="fieldManager">The field manager to write with.</param>
    /// <param name="force">Whether to force a server-side apply patch.</param>
    /// <param name="dryRun">The dry run mode.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The patched entity.</returns>
    public static Task<TEntity> SendAsync<TEntity>(
        IKubernetes apiClient,
        EntityMetadata metadata,
        V1Patch patch,
        string name,
        string? @namespace,
        string? fieldManager,
        bool? force,
        string? dryRun,
        CancellationToken cancellationToken) =>
        @namespace switch
        {
            not null => apiClient.CustomObjects.PatchNamespacedCustomObjectStatusAsync<TEntity>(
                patch,
                metadata.Group ?? string.Empty,
                metadata.Version,
                @namespace,
                metadata.PluralName,
                name,
                dryRun: dryRun,
                fieldManager: fieldManager,
                force: force,
                cancellationToken: cancellationToken),
            _ => apiClient.CustomObjects.PatchClusterCustomObjectStatusAsync<TEntity>(
                patch,
                metadata.Group ?? string.Empty,
                metadata.Version,
                metadata.PluralName,
                name,
                dryRun: dryRun,
                fieldManager: fieldManager,
                force: force,
                cancellationToken: cancellationToken),
        };

    private static bool TryCreateDiff(JsonNode? from, JsonNode? to, out JsonNode? diff)
    {
        if (from is not JsonObject fromObject || to is not JsonObject toObject)
        {
            var changed = !JsonNode.DeepEquals(from, to);
            diff = changed ? to?.DeepClone() : null;
            return changed;
        }

        var diffObject = new JsonObject();
        foreach (var (key, _) in fromObject.Where(property => !toObject.ContainsKey(property.Key)))
        {
            diffObject[key] = null;
        }

        foreach (var (key, toValue) in toObject)
        {
            if (TryCreateDiff(fromObject[key], toValue, out var valueDiff))
            {
                diffObject[key] = valueDiff;
            }
        }

        diff = diffObject;
        return diffObject.Count > 0;
    }
}
