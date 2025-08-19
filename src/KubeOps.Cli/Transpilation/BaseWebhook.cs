// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Cli.Transpilation;

internal abstract record BaseWebhook(TypeInfo Webhook, EntityMetadata Metadata)
{
    public abstract string WebhookPath { get; }

    private bool HasCreate => Webhook.DeclaredMembers.Any(m => m.Name.StartsWith("Create"));

    private bool HasUpdate => Webhook.DeclaredMembers.Any(m => m.Name.StartsWith("Update"));

    private bool HasDelete => Webhook.DeclaredMembers.Any(m => m.Name.StartsWith("Delete"));

    public string[] GetOperations() =>
        new[] { HasCreate ? "CREATE" : null, HasUpdate ? "UPDATE" : null, HasDelete ? "DELETE" : null, }
            .Where(o => o is not null).ToArray()!;
}
