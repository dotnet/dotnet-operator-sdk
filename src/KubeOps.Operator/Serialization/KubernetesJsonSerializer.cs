// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;

using k8s;

namespace KubeOps.Operator.Serialization;

/// <summary>
/// This is a wrapper around <see cref="KubernetesJson"/> to allow for
/// more flexible configuration and serialization/deserialization of Kubernetes objects.
/// </summary>
public static class KubernetesJsonSerializer
{
    private static readonly Lazy<JsonSerializerOptions> Options = new(() =>
    {
        JsonSerializerOptions options = null!;
        KubernetesJson.AddJsonOptions(c => options = c);
        return options;
    });

    public static JsonSerializerOptions SerializerOptions => Options.Value;

    public static TValue Deserialize<TValue>(string json, JsonSerializerOptions? options = null) =>
        KubernetesJson.Deserialize<TValue>(json, options ?? Options.Value);

    public static TValue Deserialize<TValue>(Stream json, JsonSerializerOptions? options = null) =>
        KubernetesJson.Deserialize<TValue>(json, options ?? Options.Value);

    public static TValue Deserialize<TValue>(JsonDocument json, JsonSerializerOptions? options = null) =>
        json.Deserialize<TValue>(options ?? Options.Value) ??
        throw new JsonException("Deserialization returned null.");

    public static TValue Deserialize<TValue>(JsonElement json, JsonSerializerOptions? options = null) =>
        json.Deserialize<TValue>(options ?? Options.Value) ??
        throw new JsonException("Deserialization returned null.");

    public static TValue Deserialize<TValue>(JsonNode json, JsonSerializerOptions? options = null) =>
        json.Deserialize<TValue>(options ?? Options.Value) ??
        throw new JsonException("Deserialization returned null.");

    public static string Serialize(object value, JsonSerializerOptions? options = null) =>
        KubernetesJson.Serialize(value, options ?? Options.Value);
}
