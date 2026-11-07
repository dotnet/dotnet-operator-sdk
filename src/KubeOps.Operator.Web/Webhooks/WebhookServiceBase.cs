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

/// <summary>
/// Registers the webhooks of the operator in the cluster.
/// </summary>
/// <param name="client">The Kubernetes client.</param>
/// <param name="loader">The loader that finds the webhooks.</param>
/// <param name="uri">The base URI the API server uses to call the operator.</param>
/// <param name="caBundle">The PEM-encoded CA bundle that validates the operator's certificate, if any.</param>
/// <param name="webhookConfigurationFactory">The factory that builds the webhook configurations.</param>
internal abstract class WebhookServiceBase(
    IKubernetesClient client,
    WebhookLoader loader,
    Uri uri,
    byte[]? caBundle,
    IWebhookConfigurationFactory webhookConfigurationFactory)
{
    private string? _validatingConfigurationName;
    private string? _mutatingConfigurationName;

    private string BasePrefix => $"{uri.AbsoluteUri.TrimEnd('/')}/";

    internal async Task RegisterAll()
    {
        await RegisterValidators();
        await RegisterMutators();
        await RegisterConverters();
    }

    internal async Task RegisterValidators()
    {
        var registrations = loader.ValidationWebhooks.Select(w => new ValidatingWebhookRegistration(
            Entities.ToEntityMetadata(w.Entity).Metadata,
            GetWebhookUri(w.Type),
            caBundle));

        var validatorConfig = webhookConfigurationFactory.CreateValidatingConfiguration(registrations);

        if (validatorConfig.Webhooks.Any())
        {
            await client.SaveAsync(validatorConfig);
            _validatingConfigurationName = validatorConfig.Name();
        }
    }

    internal async Task RegisterMutators()
    {
        var registrations = loader.MutationWebhooks.Select(w => new MutatingWebhookRegistration(
            Entities.ToEntityMetadata(w.Entity).Metadata,
            GetWebhookUri(w.Type),
            caBundle));

        var mutatorConfig = webhookConfigurationFactory.CreateMutatingConfiguration(registrations);

        if (mutatorConfig.Webhooks.Any())
        {
            await client.SaveAsync(mutatorConfig);
            _mutatingConfigurationName = mutatorConfig.Name();
        }
    }

    /// <summary>
    /// Removes the validating and mutating webhook configurations this service registered, as long as all their
    /// webhooks still point to its base URI. A configuration that another operator instance registered against its
    /// own endpoint in the meantime is kept. Conversion webhooks are written into the CRDs and stay as they are.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the configurations were checked and removed.</returns>
    internal async Task UnregisterAll(CancellationToken cancellationToken)
    {
        if (_validatingConfigurationName is { } validatingName &&
            await client.GetAsync<V1ValidatingWebhookConfiguration>(
                validatingName,
                cancellationToken: cancellationToken) is { } validating &&
            PointsToBaseUri(validating.Webhooks?.Select(w => w.ClientConfig?.Url)))
        {
            await client.DeleteAsync<V1ValidatingWebhookConfiguration>(
                validatingName,
                cancellationToken: cancellationToken);
        }

        if (_mutatingConfigurationName is { } mutatingName &&
            await client.GetAsync<V1MutatingWebhookConfiguration>(
                mutatingName,
                cancellationToken: cancellationToken) is { } mutating &&
            PointsToBaseUri(mutating.Webhooks?.Select(w => w.ClientConfig?.Url)))
        {
            await client.DeleteAsync<V1MutatingWebhookConfiguration>(
                mutatingName,
                cancellationToken: cancellationToken);
        }
    }

    internal async Task RegisterConverters()
    {
        foreach (var wh in loader.ConversionWebhooks)
        {
            var metadata = Entities.ToEntityMetadata(wh.Entity).Metadata;
            var crdName = $"{metadata.PluralName}.{metadata.Group}";

            if (await client.GetAsync<V1CustomResourceDefinition>(crdName) is not { } crd)
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
                        CaBundle = caBundle,
                    },
                },
            };

            await client.UpdateAsync(crd);
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
    /// Appends the webhook route to the base URI. Resolving the route as a relative URI would drop the path of
    /// the base URI, because webhook routes start with a slash.
    /// </summary>
    private Uri Append(string route) => new($"{BasePrefix}{route.TrimStart('/')}");

    private bool PointsToBaseUri(IEnumerable<string?>? urls) =>
        urls?.All(url => url?.StartsWith(BasePrefix, StringComparison.OrdinalIgnoreCase) == true) == true;
}
