// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;

using Json.More;
using Json.Patch;

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Method extensions for JSON diffing between two entities (<see cref="IKubernetesObject{TMetadata}"/>).
/// </summary>
[RequiresPreviewFeatures("JsonPatch extensions are a preview feature and may change in the future." +
                         "Because maybe the default filtering does not catch all volatile and non-impactful" +
                         "properties.")]
public static class JsonPatchExtensions
{
    /// <summary>
    /// <para>
    /// Ignored properties that should not be included (by default) in the JSON Patch diff.
    /// This mainly concerns metadata properties that are not relevant for the diff,
    /// like the UID of the object and resource version.
    /// </para>
    /// <para>Currently, contains the following properties:</para>
    /// <list type="bullet">
    /// <item><term>/metadata/creationTimestamp</term></item>
    /// <item><term>/metadata/deletionGracePeriodSeconds</term></item>
    /// <item><term>/metadata/deletionTimestamp</term></item>
    /// <item><term>/metadata/generation</term></item>
    /// <item><term>/metadata/managedFields</term></item>
    /// <item><term>/metadata/resourceVersion</term></item>
    /// <item><term>/metadata/selfLink</term></item>
    /// <item><term>/metadata/uid</term></item>
    /// </list>
    /// </summary>
    public static readonly string[] DefaultIgnoredProperties =
    [
        "/metadata/creationTimestamp",
        "/metadata/deletionGracePeriodSeconds",
        "/metadata/deletionTimestamp",
        "/metadata/generation",
        "/metadata/managedFields",
        "/metadata/resourceVersion",
        "/metadata/selfLink",
        "/metadata/uid",
        "/status",
        "/kind",
        "/apiVersion",
    ];

    /// <summary>
    /// Default operations filter that filters out operations that are listed in <see cref="DefaultIgnoredProperties"/>.
    /// This filters out most properties in the metadata section of the entity that are not relevant for diffing.
    /// Currently, this filters out the following properties (<see cref="DefaultIgnoredProperties"/>):
    /// <list type="bullet">
    /// <item><term>/metadata/creationTimestamp</term></item>
    /// <item><term>/metadata/deletionGracePeriodSeconds</term></item>
    /// <item><term>/metadata/deletionTimestamp</term></item>
    /// <item><term>/metadata/generation</term></item>
    /// <item><term>/metadata/managedFields</term></item>
    /// <item><term>/metadata/resourceVersion</term></item>
    /// <item><term>/metadata/selfLink</term></item>
    /// <item><term>/metadata/uid</term></item>
    /// </list>
    /// </summary>
    public static readonly Func<IReadOnlyList<PatchOperation>, IReadOnlyList<PatchOperation>> DefaultOperationsFilter =
        operations =>
            operations.Where(o => !DefaultIgnoredProperties.Any(ignored => o.Path.ToString().StartsWith(ignored)))
                .ToList();

    /// <summary>
    /// Convert a <see cref="IKubernetesObject{TMetadata}"/> into a <see cref="JsonNode"/>.
    /// </summary>
    /// <param name="entity">The entity to convert.</param>
    /// <returns>Either the json node, or null if it failed.</returns>
    public static JsonNode? ToNode(this IKubernetesObject<V1ObjectMeta> entity) =>
        JsonNode.Parse(KubernetesJson.Serialize(entity));

    /// <summary>
    /// Computes the JSON Patch diff between two Kubernetes entities implementing <see cref="IKubernetesObject{V1ObjectMeta}"/>.
    /// This method serializes both entities to JSON and calculates the difference as a JSON Patch document.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="from">The source entity to compare from.</param>
    /// <param name="to">The target entity to compare to.</param>
    /// <param name="operationsFilter">An optional filter action that filters the <see cref="PatchOperation"/>s that are contained in the <see cref="JsonPatch"/>.</param>
    /// <returns>A <see cref="JsonNode"/> representing the JSON Patch diff between the two entities.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the diff could not be created.</exception>
    public static JsonPatch CreateJsonPatch<TEntity>(
        this TEntity from,
        TEntity to,
        Func<IReadOnlyList<PatchOperation>, IReadOnlyList<PatchOperation>>? operationsFilter = null)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var fromNode = from.ToNode();
        var toNode = to.ToNode();
        var patch = fromNode.CreatePatch(toNode);

        return new JsonPatch((operationsFilter ?? DefaultOperationsFilter).Invoke(patch.Operations));
    }

    /// <summary>
    /// Checks if two Kubernetes entities implementing <see cref="IKubernetesObject{V1ObjectMeta}"/> have changes.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="from">Original object.</param>
    /// <param name="to">Changed object.</param>
    /// <returns>True if there are changes detected. Otherwise false.</returns>
    public static bool HasChanges<TEntity>(
        this TEntity from,
        TEntity to)
        where TEntity : IKubernetesObject<V1ObjectMeta> => from.CreateJsonPatch(to).Operations.Count > 0;

    /// <summary>
    /// Create a <see cref="V1Patch"/> out of a <see cref="JsonPatch"/>.
    /// This can be used to apply the patch to a Kubernetes entity using the Kubernetes client.
    /// </summary>
    /// <param name="patch">The patch that should be converted.</param>
    /// <returns>A <see cref="V1Patch"/> that may be applied to Kubernetes objects.</returns>
    public static V1Patch ToKubernetesPatch(this JsonPatch patch) =>
        new(patch.ToJsonString(), V1Patch.PatchType.JsonPatch);

    /// <summary>
    /// Create the unformatted JSON string representation of a <see cref="JsonPatch"/>.
    /// </summary>
    /// <param name="patch">The <see cref="JsonPatch"/> to convert.</param>
    /// <returns>A string that represents the unformatted JSON representation of the patch.</returns>
    public static string ToJsonString(this JsonPatch patch) => patch.ToJsonDocument().RootElement.GetRawText();

    /// <summary>
    /// Create the base 64 representation of a <see cref="JsonPatch"/>.
    /// </summary>
    /// <param name="patch">The patch to convert.</param>
    /// <returns>The base64 encoded representation of the patch.</returns>
    public static string ToBase64String(this JsonPatch patch) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.ToJsonString()));
}
