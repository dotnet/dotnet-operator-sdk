// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s.Models;

using KubeOps.Abstractions.Webhooks;
using KubeOps.KubernetesClient;
using KubeOps.Transpiler;

using Microsoft.AspNetCore.Mvc;

namespace KubeOps.Operator.Web.Webhooks;

internal abstract class WebhookServiceBase(IKubernetesClient client, WebhookLoader loader, Uri uri, IWebhookConfigurationFactory webhookConfigurationFactory)
{
    /// <summary>
    /// The URI the webhooks will use to connect to the operator.
    /// </summary>
    private protected Uri Uri { get; } = uri;

    private protected IKubernetesClient Client { get; } = client;

    /// <summary>
    /// The PEM-encoded CA bundle for validating the webhook's certificate.
    /// </summary>
    private protected byte[]? CaBundle { get; set; }

    internal async Task RegisterAll()
    {
        await RegisterValidators();
        await RegisterMutators();
        await RegisterConverters();
    }

    internal async Task RegisterValidators()
    {
        var registrations = loader
            .ValidationWebhooks
            .Select(w => (
                Uri: GetWebhookUri(w.Type),
                Entities.ToEntityMetadata(w.Entity).Metadata))
            .Select(hook => new ValidatingWebhookRegistration(
                hook.Metadata,
                hook.Uri,
                CaBundle));

        var validatorConfig = webhookConfigurationFactory.CreateValidatingConfiguration(registrations);

        if (validatorConfig.Webhooks.Any())
        {
            await Client.SaveAsync(validatorConfig);
        }
    }

    internal async Task RegisterMutators()
    {
        var registrations = loader
            .MutationWebhooks
            .Select(w => (
                Uri: GetWebhookUri(w.Type),
                Entities.ToEntityMetadata(w.Entity).Metadata))
            .Select(hook => new MutatingWebhookRegistration(
                hook.Metadata,
                hook.Uri,
                CaBundle));

        var mutatorConfig = webhookConfigurationFactory.CreateMutatingConfiguration(registrations);

        if (mutatorConfig.Webhooks.Any())
        {
            await Client.SaveAsync(mutatorConfig);
        }
    }

    internal async Task RegisterConverters()
    {
        foreach (var wh in loader.ConversionWebhooks)
        {
            var metadata = Entities.ToEntityMetadata(wh.Entity).Metadata;
            var crdName = $"{metadata.PluralName}.{metadata.Group}";

            if (await Client.GetAsync<V1CustomResourceDefinition>(crdName) is not { } crd)
            {
                continue;
            }

            var webhookUri = GetWebhookUri(wh.Type);

            crd.Spec.Conversion = new()
            {
                Strategy = "Webhook",
                Webhook = new()
                {
                    ConversionReviewVersions = ["v1"],
                    ClientConfig = new()
                    {
                        Url = webhookUri.ToString(),
                        CaBundle = CaBundle,
                    },
                },
            };

            await Client.UpdateAsync(crd);
        }
    }

    private Uri GetWebhookUri(TypeInfo wh)
    {
        var webhookAttribute = wh.GetCustomAttributes().OfType<IWebhookAttribute>().FirstOrDefault();

        if (webhookAttribute is not null)
        {
            return Append(webhookAttribute.Uri.OriginalString);
        }

        var routeAttribute = wh.GetCustomAttribute<RouteAttribute>();

        if (routeAttribute is { Template: not null and not "" })
        {
            return Append(routeAttribute.Template);
        }

        throw new InvalidOperationException(
            $"No {nameof(IWebhookAttribute)} or {nameof(RouteAttribute)} with a valid Uri found on webhook class {wh.FullName}. " +
            $"Add one of these attributes to specify the webhook's relative URI.");
    }

    /// <summary>
    /// Appends the webhook route to <see cref="Uri"/>. Resolving the route as a relative URI would drop the path of
    /// the base URI, because webhook routes start with a slash.
    /// </summary>
    private Uri Append(string route) => new($"{Uri.AbsoluteUri.TrimEnd('/')}/{route.TrimStart('/')}");
}
