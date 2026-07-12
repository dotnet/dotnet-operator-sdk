// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Text.Json;

using k8s;
using k8s.Models;

using Microsoft.AspNetCore.Mvc;

namespace KubeOps.Operator.Web.Webhooks.Conversion;

/// <summary>
/// Base class for conversion webhooks. This class handles the conversion of
/// entities in their versions. Must be annotated with the <see cref="ConversionWebhookAttribute"/>.
/// </summary>
/// <typeparam name="TEntity">The target type (version) of the entity.</typeparam>
[RequiresPreviewFeatures(
    "Conversion webhooks API is not yet stable, the way that conversion " +
    "webhooks are implemented may change in the future based on user feedback.")]
[ApiController]
public abstract class ConversionWebhook<TEntity> : ControllerBase
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private JsonSerializerOptions _serializerOptions = null!;

    protected ConversionWebhook()
    {
        KubernetesJson.AddJsonOptions(c => _serializerOptions = c);
    }

    /// <summary>
    /// The list of converters that are available for this webhook.
    /// </summary>
    protected abstract IEnumerable<IEntityConverter<TEntity>> Converters { get; }

    private IEnumerable<(string To, string From, Func<object, object> Converter, Type FromType)> AvailableConversions =>
        Converters
            .SelectMany(c => new (string, string, Func<object, object>, Type)[]
            {
                (c.ToGroupVersion, c.FromGroupVersion, o => c.Convert(o), c.FromType),
                (c.FromGroupVersion, c.ToGroupVersion, o => c.Revert((TEntity)o), c.ToType),
            });

    [HttpPost]
    public IActionResult Convert([FromBody] ConversionRequest request)
    {
        try
        {
            var conversions = AvailableConversions.ToList();

            // The conversion chain depends only on the source and desired versions, and the desired version is constant
            // for the whole request, so the chain is cached by source apiVersion. A LIST response typically converts
            // many objects that share the same stored version, so this avoids repeating the graph search (and the scans
            // over the conversions list) for every object. Unroutable source versions are cached as null so they are
            // searched only once as well.
            var paths = new Dictionary<string, (IReadOnlyList<Func<object, object>> Steps, Type SourceType)?>();

            var results = new List<object>();
            foreach (var obj in request.Request.Objects)
            {
                if (obj["apiVersion"]?.GetValue<string>() is not { } sourceApiVersion)
                {
                    continue;
                }

                if (!paths.TryGetValue(sourceApiVersion, out var path))
                {
                    path = TryBuildConversionPath(conversions, sourceApiVersion, request.Request.DesiredApiVersion, out var steps, out var sourceType)
                        ? (steps, sourceType)
                        : null;
                    paths[sourceApiVersion] = path;
                }

                if (path is not { } route)
                {
                    continue;
                }

                var converted = obj.Deserialize(route.SourceType, _serializerOptions)!;
                foreach (var step in route.Steps)
                {
                    converted = step(converted);
                }

                results.Add(converted);
            }

            return new ConversionResponse(request.Request.Uid, results);
        }
        catch (Exception e)
        {
            return new ConversionResponse(request.Request.Uid, e.ToString());
        }
    }

    /// <summary>
    /// Builds a chain of registered converters that transforms an object from <paramref name="from"/> to
    /// <paramref name="to"/>. Converters are registered as a hub-and-spoke set (every served version converts to and
    /// from the storage/hub version), so the API server can legitimately request a conversion between two versions that
    /// have no direct converter — e.g. an object persisted under a version that is no longer the storage version, read
    /// at a third served version. In that case the conversion is composed through intermediate versions (a
    /// breadth-first search picks the shortest chain), rather than the object being silently dropped.
    /// </summary>
    /// <param name="conversions">The directed conversion edges available for this webhook.</param>
    /// <param name="from">The <c>apiVersion</c> the source object is encoded in.</param>
    /// <param name="to">The requested <c>desiredAPIVersion</c>.</param>
    /// <param name="steps">The ordered conversion functions to apply to the deserialized source object.</param>
    /// <param name="sourceType">The CLR type the source object must be deserialized into before applying the steps.</param>
    /// <returns><see langword="true"/> if a conversion chain exists; otherwise <see langword="false"/>.</returns>
    private static bool TryBuildConversionPath(
        IReadOnlyList<(string To, string From, Func<object, object> Converter, Type FromType)> conversions,
        string from,
        string to,
        out IReadOnlyList<Func<object, object>> steps,
        [NotNullWhen(true)] out Type? sourceType)
    {
        steps = [];
        sourceType = null;

        // Nothing to convert; also guards against walking an edge back to the source through the hub.
        if (from == to)
        {
            return false;
        }

        // Breadth-first search over the directed conversion edges so the shortest chain is chosen. A direct converter,
        // when one exists, is found as a single-hop path and therefore keeps the previous behaviour unchanged.
        var queue = new Queue<List<(string To, string From, Func<object, object> Converter, Type FromType)>>();
        var visited = new HashSet<string> { from };

        foreach (var edge in conversions.Where(c => c.From == from))
        {
            queue.Enqueue([edge]);
        }

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var last = path[^1];

            if (last.To == to)
            {
                steps = [.. path.Select(e => e.Converter)];
                sourceType = path[0].FromType;
                return true;
            }

            if (!visited.Add(last.To))
            {
                continue;
            }

            foreach (var edge in conversions.Where(c => c.From == last.To))
            {
                queue.Enqueue([.. path, edge]);
            }
        }

        return false;
    }
}
