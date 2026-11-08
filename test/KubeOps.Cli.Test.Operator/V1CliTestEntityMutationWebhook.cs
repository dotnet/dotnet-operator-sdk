// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks.Admission.Mutation;

namespace KubeOps.Cli.Test.Operator;

[MutationWebhook(typeof(V1CliTestEntity))]
public sealed class V1CliTestEntityMutationWebhook : MutationWebhook<V1CliTestEntity>
{
    public override MutationResult<V1CliTestEntity> Create(V1CliTestEntity entity, bool dryRun) => NoChanges();
}
