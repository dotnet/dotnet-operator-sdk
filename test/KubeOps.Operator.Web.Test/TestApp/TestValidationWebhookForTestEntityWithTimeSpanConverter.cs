// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks.Admission.Validation;

namespace KubeOps.Operator.Web.Test.TestApp;

[ValidationWebhook(typeof(TestEntityWithTimeSpanConverter))]
public sealed class TestValidationWebhookForTestEntityWithTimeSpanConverter : ValidationWebhook<TestEntityWithTimeSpanConverter>
{
    public override ValidationResult Create(TestEntityWithTimeSpanConverter entity, bool dryRun)
        => dryRun ? Success("dry-run") : Success();

    public override ValidationResult Update(TestEntityWithTimeSpanConverter oldentity, TestEntityWithTimeSpanConverter newEntity, bool dryRun)
    {
        return newEntity.Spec.Timeout < oldentity.Spec.Timeout
            ? Fail("timeout-shortened")
            : Success();
    }

    public override ValidationResult Delete(TestEntityWithTimeSpanConverter entity, bool dryRun)
        => Success("delete-validated");
}
