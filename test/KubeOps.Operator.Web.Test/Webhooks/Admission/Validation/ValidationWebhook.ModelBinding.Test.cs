// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks.Admission;

using Microsoft.AspNetCore.TestHost;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission.Validation;

public sealed class ValidationWebhookModelBindingTest : WebhookTestBase
{
    [Theory(DisplayName = "Validation webhook binds request correctly")]
    [Trait("Category", "ValidationWebhookModelBinding")]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-create-iso-uid", "CREATE", false, "createvalue", "PT10M", true)]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-update-iso-uid", "UPDATE", false, "same", "PT1H30M", true)]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-update-iso-uid-fail", "UPDATE", false, "new", "PT1H30M", false)]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-delete-iso-uid", "DELETE", false, "deletedvalue", "PT20M", true)]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-dryrun-iso-uid", "CREATE", true, "dryrunvalue", "PT5M", true)]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-dryrun-update-iso-uid", "UPDATE", true, "old", "PT5M", true)]
    [InlineData(nameof(TestEntityWithTimeSpanConverter), "test-create-converter-uid", "CREATE", false, "createvalue", "00:10:00", true)]
    [InlineData(nameof(TestEntityWithTimeSpanConverter), "test-update-converter-uid-ok", "UPDATE", false, "newvalue", "01:00:00", true)]
    [InlineData(nameof(TestEntityWithTimeSpanConverter), "test-update-converter-uid-fail", "UPDATE", false, "newvalue", "00:30:00", false)]
    [InlineData(nameof(TestEntityWithTimeSpanConverter), "test-delete-converter-uid", "DELETE", false, "deletedvalue", "00:20:00", true)]
    [InlineData(nameof(TestEntityWithTimeSpanConverter), "test-dryrun-converter-uid", "CREATE", true, "dryrunvalue", "00:10:00", true)]
    public async Task HandleAsync_Request_BindsAndValidatesCorrectly(
        string entityType,
        string uid,
        string operation,
        bool dryRun,
        string value,
        string timeout,
        bool expectedAllowed)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        // Specific values for validation logic in TestApp:
        // For TestEntityWithISODurationTimeSpan, UPDATE fails if value changes from "old" to "new" (mocked by using these in InlineData if needed)
        // For TestEntityWithTimeSpanConverter, UPDATE fails if timeout is shortened.
        string oldValue = value;
        string newValue = value;
        string oldTimeout = timeout;
        string newTimeout = timeout;

        if (uid == "test-update-iso-uid-fail")
        {
            oldValue = "old";
            newValue = "new";
        }
        else if (uid == "test-update-converter-uid-ok")
        {
            oldTimeout = "00:45:00";
            newTimeout = "01:00:00";
        }
        else if (uid == "test-update-converter-uid-fail")
        {
            oldTimeout = "00:45:00";
            newTimeout = "00:30:00";
        }

        var @object = CreateTestSpec(newValue, newTimeout);
        var oldObject = CreateTestSpec(oldValue, oldTimeout);
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: operation,
            dryRun: dryRun,
            @object: operation == "DELETE" ? null : @object,
            oldObject: operation == "CREATE" ? null : oldObject);

        var response = await PostWebhookAsync(
            client,
            $"/validate/{entityType.ToLowerInvariant()}",
            admissionRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        if (dryRun)
        {
            var result = await response.Content.ReadFromJsonAsync<AdmissionResponse>(
                cancellationToken: TestContext.Current.CancellationToken);
            result.Should().NotBeNull();
            result.Response.Allowed.Should().Be(expectedAllowed);
            result.Response.Warnings.Should().Contain("dry-run");
        }
        else
        {
            var result = await response.Content.ReadFromJsonAsync<AdmissionResponse>(
                cancellationToken: TestContext.Current.CancellationToken);
            result.Should().NotBeNull();
            result.Response.Uid.Should().Be(uid);
            result.Response.Allowed.Should().Be(expectedAllowed);
        }
    }
}
