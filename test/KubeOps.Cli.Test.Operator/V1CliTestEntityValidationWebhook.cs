// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks.Admission.Validation;

namespace KubeOps.Cli.Test.Operator;

[ValidationWebhook(typeof(V1CliTestEntity))]
public sealed class V1CliTestEntityValidationWebhook : ValidationWebhook<V1CliTestEntity>
{
    public override ValidationResult Create(V1CliTestEntity entity, bool dryRun) => Success();

    public override ValidationResult Update(V1CliTestEntity oldEntity, V1CliTestEntity newEntity, bool dryRun)
        => Success();
}
