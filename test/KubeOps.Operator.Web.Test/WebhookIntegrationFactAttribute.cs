// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;

namespace KubeOps.Operator.Web.Test;

/// <summary>
/// A fact that needs the Kubernetes API server to call the test app. It is skipped unless
/// <see cref="ExternalWebhookTestEndpoint.IsConfigured"/>.
/// </summary>
public sealed class WebhookIntegrationFactAttribute : FactAttribute
{
    public WebhookIntegrationFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = ExternalWebhookTestEndpoint.SkipReason;
        SkipUnless = nameof(ExternalWebhookTestEndpoint.IsConfigured);
        SkipType = typeof(ExternalWebhookTestEndpoint);
    }
}
