// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks.Admission.Mutation;

namespace KubeOps.Operator.Web.Test.TestApp;

[MutationWebhook(typeof(TestEntityWithTimeSpanConverter))]
public sealed class TestMutationWebhookForTestEntityWithTimeSpanConverter : MutationWebhook<TestEntityWithTimeSpanConverter>
{
    public override MutationResult<TestEntityWithTimeSpanConverter> Create(TestEntityWithTimeSpanConverter entity, bool dryRun)
    {
        if (dryRun)
        {
            return NoChanges("dry-run");
        }

        entity.Spec.Value = "mutated";
        entity.Spec.Timeout = TimeSpan.FromHours(1);
        return Modified(entity);
    }

    public override MutationResult<TestEntityWithTimeSpanConverter> Update(TestEntityWithTimeSpanConverter oldEntity, TestEntityWithTimeSpanConverter newEntity, bool dryRun)
    {
        newEntity.Spec.Value = $"updated-from-{oldEntity.Spec.Value}";
        return Modified(newEntity);
    }

    public override MutationResult<TestEntityWithTimeSpanConverter> Delete(TestEntityWithTimeSpanConverter entity, bool dryRun)
    {
        entity.Spec.Value = "deleted";
        return Modified(entity);
    }
}
