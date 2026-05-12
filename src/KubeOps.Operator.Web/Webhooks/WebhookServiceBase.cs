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

internal abstract class WebhookServiceBase(IKubernetesClient client, WebhookLoader loader, WebhookConfig config, IWebhookConfigurationFactory webhookConfigurationFactory)
{
    /// <summary>
    /// The URI the webhooks will use to connect to the operator.
    /// </summary>
    private protected virtual Uri Uri { get; set; } = new($"https://{config.Hostname}:{config.Port}");

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
            .Select(t => (
                Uri: GetWebhookUri(t),
                Entities.ToEntityMetadata(t.BaseType!.GenericTypeArguments[0]).Metadata))
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
            .Select(t => (
                Uri: GetWebhookUri(t),
                Entities.ToEntityMetadata(t.BaseType!.GenericTypeArguments[0]).Metadata))
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
        var conversionWebhooks = loader.ConversionWebhooks.ToList();
        if (conversionWebhooks.Count == 0)
        {
            return;
        }

        foreach (var wh in conversionWebhooks)
        {
            var metadata = Entities.ToEntityMetadata(wh.BaseType!.GenericTypeArguments[0]).Metadata;
            var crdName = $"{metadata.PluralName}.{metadata.Group}";

            if (await Client.GetAsync<V1CustomResourceDefinition>(crdName) is not { } crd)
            {
                continue;
            }

            var webhookUri = GetWebhookUri(wh);

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
            return new Uri(
                Uri,
                webhookAttribute.Uri);
        }

        var routeAttribute = wh.GetCustomAttribute<RouteAttribute>();

        if (routeAttribute is { Template: not null and not "" })
        {
            return new Uri(
                Uri,
                routeAttribute.Template);
        }

        throw new InvalidOperationException(
            $"No {nameof(IWebhookAttribute)} or {nameof(RouteAttribute)} with a valid Uri found on webhook class {wh.FullName}. " +
            $"Add one of these attributes to specify the webhook's relative URI.");
    }
}
