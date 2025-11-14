// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks.Admission.Mutation;

using WebhookOperator.Entities;

namespace WebhookOperator.Webhooks;

[MutationWebhook(typeof(V1TestEntity))]
public sealed class TestMutationWebhook : MutationWebhook<V1TestEntity>
{
    public override MutationResult<V1TestEntity> Create(V1TestEntity entity, bool dryRun)
    {
        if (entity.Spec.Username == "overwrite")
        {
            entity.Spec.Username = "random overwritten";
            return Modified(entity);
        }

        return NoChanges();
    }
}
