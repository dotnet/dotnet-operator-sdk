// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks.Admission.Validation;

namespace KubeOps.Operator.Web.Test.TestApp;

[ValidationWebhook(typeof(V1OperatorWebIntegrationTestEntity))]
public class TestValidationWebhook : ValidationWebhook<V1OperatorWebIntegrationTestEntity>
{
    public override ValidationResult Create(V1OperatorWebIntegrationTestEntity entity, bool dryRun)
    {
        if (entity.Spec.Username == "forbidden")
        {
            return Fail("name may not be 'forbidden'.", 422);
        }

        return Success();
    }
}
