// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Cli.Transpilation;

internal record ValidationWebhook(TypeInfo Validator, EntityMetadata Metadata) : BaseWebhook(Validator, Metadata)
{
    public override string WebhookPath =>
        $"/validate/{Validator.BaseType!.GenericTypeArguments[0].Name.ToLowerInvariant()}";
}
