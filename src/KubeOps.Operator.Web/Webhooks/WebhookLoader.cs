// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using KubeOps.Operator.Web.Webhooks.Admission.Mutation;
using KubeOps.Operator.Web.Webhooks.Admission.Validation;
using KubeOps.Operator.Web.Webhooks.Conversion;

namespace KubeOps.Operator.Web.Webhooks;

internal sealed class WebhookLoader(IEnumerable<TypeInfo> types)
{
    public WebhookLoader(Assembly entry)
        : this(entry.DefinedTypes)
    {
    }

    public IEnumerable<WebhookType> ValidationWebhooks => Find(typeof(ValidationWebhook<>));

    public IEnumerable<WebhookType> MutationWebhooks => Find(typeof(MutationWebhook<>));

#pragma warning disable CA2252 // This is internal only.
    public IEnumerable<WebhookType> ConversionWebhooks => Find(typeof(ConversionWebhook<>));
#pragma warning restore CA2252

    /// <summary>
    /// Returns the entity type of the closest base class built from <paramref name="webhookDefinition"/>, so webhooks
    /// derived from an intermediate base class are found as well.
    /// </summary>
    private static Type? EntityType(Type type, Type webhookDefinition)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == webhookDefinition)
            {
                return baseType.GenericTypeArguments[0];
            }
        }

        return null;
    }

    private IEnumerable<WebhookType> Find(Type webhookDefinition) =>
        from type in types
        where type is { IsAbstract: false, ContainsGenericParameters: false }
        let entity = EntityType(type, webhookDefinition)
        where entity is not null
        select new WebhookType(type, entity);
}
