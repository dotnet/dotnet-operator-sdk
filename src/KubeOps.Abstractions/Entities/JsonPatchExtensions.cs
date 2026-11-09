// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Method extensions for JSON diffing between two entities (<see cref="IKubernetesObject{TMetadata}"/>).
/// </summary>
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
            operations.Where(o => !DefaultIgnoredProperties.Any(ignored => o.Path.StartsWith(ignored)))
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
    /// Computes the JSON Patch diff between two JSON nodes.
    /// Objects are compared by key. Arrays are compared by index, and an array that
    /// becomes empty or stops being empty is replaced as a whole.
    /// </summary>
    /// <param name="from">The source node to compare from.</param>
    /// <param name="to">The target node to compare to.</param>
    /// <returns>The JSON Patch that transforms the source node into the target node.</returns>
    public static JsonPatch CreatePatch(this JsonNode? from, JsonNode? to)
    {
        var operations = new List<PatchOperation>();
        AddOperations(operations, from, to, string.Empty);
        return new JsonPatch(operations);
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
    public static string ToJsonString(this JsonPatch patch)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var operation in patch.Operations)
            {
                WriteOperation(writer, operation);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Create the base 64 representation of a <see cref="JsonPatch"/>.
    /// </summary>
    /// <param name="patch">The patch to convert.</param>
    /// <returns>The base64 encoded representation of the patch.</returns>
    public static string ToBase64String(this JsonPatch patch) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.ToJsonString()));

    private static void AddOperations(List<PatchOperation> operations, JsonNode? from, JsonNode? to, string path)
    {
        switch (from, to)
        {
            case (JsonObject fromObject, JsonObject toObject):
                AddObjectOperations(operations, fromObject, toObject, path);
                break;
            case (JsonArray fromArray, JsonArray toArray):
                AddArrayOperations(operations, fromArray, toArray, path);
                break;
            default:
                if (!IsEquivalent(from, to))
                {
                    operations.Add(PatchOperation.Replace(path, to));
                }

                break;
        }
    }

    private static void AddObjectOperations(
        List<PatchOperation> operations,
        JsonObject from,
        JsonObject to,
        string path)
    {
        foreach (var (key, fromValue) in from)
        {
            if (to.TryGetPropertyValue(key, out var toValue))
            {
                AddOperations(operations, fromValue, toValue, CombinePath(path, key));
            }
            else
            {
                operations.Add(PatchOperation.Remove(CombinePath(path, key)));
            }
        }

        foreach (var (key, toValue) in to)
        {
            if (!from.ContainsKey(key))
            {
                operations.Add(PatchOperation.Add(CombinePath(path, key), toValue));
            }
        }
    }

    private static void AddArrayOperations(
        List<PatchOperation> operations,
        JsonArray from,
        JsonArray to,
        string path)
    {
        if ((from.Count == 0) != (to.Count == 0))
        {
            operations.Add(PatchOperation.Replace(path, to));
            return;
        }

        if (to.Count >= from.Count)
        {
            for (var i = 0; i < to.Count; i++)
            {
                AddArrayElementOperation(operations, from, to, i, CombinePath(path, i));
            }

            return;
        }

        // Descending, because every removal shifts the elements behind it.
        for (var i = from.Count - 1; i >= 0; i--)
        {
            AddArrayElementOperation(operations, from, to, i, CombinePath(path, i));
        }
    }

    private static void AddArrayElementOperation(
        List<PatchOperation> operations,
        JsonArray from,
        JsonArray to,
        int index,
        string path)
    {
        if (index >= to.Count)
        {
            operations.Add(PatchOperation.Remove(path));
        }
        else if (index >= from.Count)
        {
            operations.Add(PatchOperation.Add(path, to[index]));
        }
        else
        {
            AddOperations(operations, from[index], to[index], path);
        }
    }

    private static string CombinePath(string path, string key) =>
        $"{path}/{key.Replace("~", "~0").Replace("/", "~1")}";

    private static string CombinePath(string path, int index) => $"{path}/{index}";

    private static bool IsEquivalent(JsonNode? left, JsonNode? right) => (left, right) switch
    {
        (null, null) => true,
        (JsonValue leftValue, JsonValue rightValue) => IsEquivalent(ToElement(leftValue), ToElement(rightValue)),
        _ => false,
    };

    private static bool IsEquivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            // Numbers compare by value (1 equals 1.0), the text is only the fallback outside the decimal range.
            JsonValueKind.Number when left.TryGetDecimal(out var leftNumber) && right.TryGetDecimal(out var rightNumber)
                => leftNumber == rightNumber,
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.String => left.GetString() == right.GetString(),
            _ => true,
        };
    }

    private static JsonElement ToElement(JsonValue value)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element;
        }

        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.Clone();
    }

    private static void WriteOperation(Utf8JsonWriter writer, PatchOperation operation)
    {
        writer.WriteStartObject();
        writer.WriteString("op", GetOperationName(operation.Op));
        writer.WriteString("path", operation.Path);

        switch (operation.Op)
        {
            case OperationType.Add or OperationType.Replace or OperationType.Test:
                writer.WritePropertyName("value");
                if (operation.Value is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    operation.Value.WriteTo(writer);
                }

                break;
            case OperationType.Move or OperationType.Copy:
                writer.WriteString("from", operation.From);
                break;
        }

        writer.WriteEndObject();
    }

    private static string GetOperationName(OperationType type) => type switch
    {
        OperationType.Add => "add",
        OperationType.Remove => "remove",
        OperationType.Replace => "replace",
        OperationType.Move => "move",
        OperationType.Copy => "copy",
        OperationType.Test => "test",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown JSON Patch operation type."),
    };
}
