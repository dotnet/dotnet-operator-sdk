// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;

namespace KubeOps.Operator.Web.Webhooks;

internal sealed record ExternalWebhookEndpoint(Uri BaseUri, byte[]? CaBundle)
{
    internal const string ConfigurationKey = "KubeOps:Webhooks:ExternalUrl";

    internal static ExternalWebhookEndpoint Create(Uri baseUri, byte[]? caBundle)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        if (!IsValid(baseUri))
        {
            throw new ArgumentException(
                $"The external webhook endpoint '{baseUri}' must be an absolute https URL without query or fragment.",
                nameof(baseUri));
        }

        return new(baseUri, caBundle);
    }

    internal static ExternalWebhookEndpoint FromConfiguration(IConfiguration configuration)
    {
        var value = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            var environmentVariable = ConfigurationKey.Replace(":", "__", StringComparison.Ordinal);
            throw new InvalidOperationException(
                $"The external webhook endpoint is not configured. Set '{ConfigurationKey}' (environment variable " +
                $"'{environmentVariable}') to the public https URL of the tunnel.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var baseUri) || !IsValid(baseUri))
        {
            throw new InvalidOperationException(
                $"The configured external webhook endpoint '{value}' ({ConfigurationKey}) must be an absolute " +
                "https URL without query or fragment.");
        }

        return new(baseUri, null);
    }

    // The API server only calls webhook URLs with the https scheme and without query or fragment.
    private static bool IsValid(Uri baseUri) =>
        baseUri.IsAbsoluteUri &&
        baseUri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(baseUri.Query) &&
        string.IsNullOrEmpty(baseUri.Fragment);
}
