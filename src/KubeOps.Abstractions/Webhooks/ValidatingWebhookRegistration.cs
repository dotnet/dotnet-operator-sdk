// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Entities;

namespace KubeOps.Abstractions.Webhooks;

/// <summary>
/// Registration data for a validating webhook.
/// </summary>
/// <param name="Metadata">The entity metadata for the webhook's target resource.</param>
/// <param name="Uri">The absolute URI the webhook is reachable at.</param>
/// <param name="CaBundle">The PEM-encoded CA bundle for validating the webhook's certificate, or null.</param>
public record ValidatingWebhookRegistration(
    EntityMetadata Metadata,
    Uri Uri,
    byte[]? CaBundle);
