// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Operator.Web.Webhooks;

/// <summary>
/// Interface for webhook attributes that define the URI a webhook is reachable at.
/// Implement this interface on a custom attribute to control the webhook's registered URI.
/// </summary>
public interface IWebhookAttribute
{
    /// <summary>
    /// Gets the relative URI the webhook is reachable at.
    /// </summary>
    Uri Uri { get; }
}
