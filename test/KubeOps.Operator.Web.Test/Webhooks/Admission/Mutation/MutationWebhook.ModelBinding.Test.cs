// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks.Admission;

using Microsoft.AspNetCore.TestHost;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission.Mutation;

public sealed class MutationWebhookModelBindingTest : WebhookTestBase
{
    [Theory(DisplayName = "Mutation webhook binds request correctly")]
    [Trait("Category", "MutationWebhookModelBinding")]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-create-iso-uid", "CREATE", false, "createvalue", "PT10M")]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-update-iso-uid", "UPDATE", false, "newvalue", "PT1H30M")]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-delete-iso-uid", "DELETE", false, "deletedvalue", "PT20M")]
    [InlineData(nameof(TestEntityWithISODurationTimeSpan), "test-dryrun-iso-uid", "CREATE", true, "dryrunvalue", "PT5M")]
    [InlineData(nameof(TestEntityWithTimeSpanConverter), "test-create-converter-uid", "CREATE", false, "createvalue", "00:10:00")]
    [InlineData(nameof(TestEntityWithTimeSpanConverter), "test-update-converter-uid", "UPDATE", false, "newvalue", "01:30:00")]
    [InlineData(nameof(TestEntityWithTimeSpanConverter), "test-delete-converter-uid", "DELETE", false, "deletedvalue", "00:20:00")]
    [InlineData(nameof(TestEntityWithTimeSpanConverter), "test-dryrun-converter-uid", "CREATE", true, "dryrunvalue", "00:05:00")]
    public async Task HandleAsync_Request_BindsAndMutatesCorrectly(
        string entityType,
        string uid,
        string operation,
        bool dryRun,
        string value,
        string timeout)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec(value, timeout);
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: operation,
            dryRun: dryRun,
            @object: operation == "DELETE" ? null : spec,
            oldObject: operation == "CREATE" ? null : spec);

        var response = await PostWebhookAsync(
            client,
            $"/mutate/{entityType.ToLowerInvariant()}",
            admissionRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content
            .ReadFromJsonAsync<AdmissionResponse>(
                cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();

        result.Response.Uid.Should().Be(uid);
        result.Response.Allowed.Should().BeTrue();

        if (dryRun)
        {
            result.Response.Warnings.Should().Contain("dry-run");
            result.Response.Patch.Should().BeNull("no patch should be returned during dry run if the webhook respects the flag");
        }
        else
        {
            result.Response.Patch.Should().NotBeNull("the mutation webhook should return a patch for modified entities");
        }
    }
}
