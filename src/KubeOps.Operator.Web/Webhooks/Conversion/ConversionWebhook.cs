// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

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
            var toConverters = AvailableConversions
                .Where(c => c.To == request.Request.DesiredApiVersion)
                .ToList();
            var results = new List<object>();
            foreach (var obj in request.Request.Objects)
            {
                if (obj["apiVersion"]?.GetValue<string>() is not { } targetApiVersion ||
                    toConverters.TrueForAll(c => c.From != targetApiVersion))
                {
                    continue;
                }

                var (_, _, converter, type) = toConverters.Find(c => c.From == targetApiVersion);

                results.Add(converter(obj.Deserialize(type, _serializerOptions)!));
            }

            return new ConversionResponse(request.Request.Uid, results);
        }
        catch (Exception e)
        {
            return new ConversionResponse(request.Request.Uid, e.ToString());
        }
    }
}
