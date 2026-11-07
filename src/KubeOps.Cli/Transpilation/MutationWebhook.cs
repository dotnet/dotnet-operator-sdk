// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Cli.Transpilation;

internal record MutationWebhook(TypeInfo Webhook, Type Entity, EntityMetadata Metadata) : BaseWebhook(Webhook, Metadata)
{
    public override string WebhookPath => $"/mutate/{Entity.Name.ToLowerInvariant()}";
}
