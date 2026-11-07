// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks;

namespace KubeOps.Operator.Web.Test.TestApp;

internal static class TestWebhooks
{
    /// <summary>
    /// Gets a loader for the webhooks of the test app. Webhook types that individual tests declare are left out.
    /// </summary>
    public static WebhookLoader Loader => new(typeof(TestWebhooks).Assembly.DefinedTypes
        .Where(t => t.Namespace == typeof(TestWebhooks).Namespace));
}
