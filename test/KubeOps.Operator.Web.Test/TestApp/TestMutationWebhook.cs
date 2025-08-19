// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks.Admission.Mutation;

namespace KubeOps.Operator.Web.Test.TestApp;

[MutationWebhook(typeof(V1OperatorWebIntegrationTestEntity))]
public class TestMutationWebhook : MutationWebhook<V1OperatorWebIntegrationTestEntity>
{
    public override MutationResult<V1OperatorWebIntegrationTestEntity> Create(V1OperatorWebIntegrationTestEntity entity,
        bool dryRun)
    {
        if (entity.Spec.Username == "overwrite")
        {
            entity.Spec.Username = "overwritten";
            return Modified(entity);
        }

        return NoChanges();
    }
}
