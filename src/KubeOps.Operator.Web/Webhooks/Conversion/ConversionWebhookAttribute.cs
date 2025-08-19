// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.Versioning;

using KubeOps.Transpiler;

using Microsoft.AspNetCore.Mvc;

namespace KubeOps.Operator.Web.Webhooks.Conversion;

/// <summary>
/// Defines (marks) an MVC controller as "conversion webhook". The route is automatically set to
/// <c>/convert/[group]/[plural-name]</c>.
/// This must be used in conjunction with the <see cref="ConversionWebhook{TEntity}"/> class.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
[RequiresPreviewFeatures(
    "Conversion webhooks API is not yet stable, the way that conversion " +
    "webhooks are implemented may change in the future based on user feedback.")]
public class ConversionWebhookAttribute : RouteAttribute
{
    public ConversionWebhookAttribute(Type entityType)
        : base(GetRouteTemplate(entityType))
    {
    }

    public ConversionWebhookAttribute(string group, string pluralName)
        : base($"/convert/{group}/{pluralName}")
    {
    }

    private static string GetRouteTemplate(Type entityType)
    {
        var meta = Entities.ToEntityMetadata(entityType).Metadata;
        return $"/convert/{meta.Group}/{meta.PluralName}";
    }
}
