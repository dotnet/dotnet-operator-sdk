// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net.Mime;
using System.Text;

using k8s;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission;

public abstract class WebhookTestBase
{
    protected static object CreateTestSpec(string value, string timeout)
        => new
        {
            apiVersion = "test.kubeops.dev/v1",
            kind = "TestEntity",
            metadata = new { name = "test-entity", @namespace = "default" },
            spec = new { value, timeout },
        };

    protected static object CreateAdmissionReview(
        string uid,
        string operation,
        bool dryRun,
        object? @object = null,
        object? oldObject = null)
        => new
        {
            apiVersion = "admission.k8s.io/v1",
            kind = "AdmissionReview",
            request = new
            {
                uid,
                operation,
                dryRun,
                @object,
                oldObject,
            },
        };

    protected static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string path,
        object admissionRequest)
    {
        var json = KubernetesJson.Serialize(admissionRequest);

        return await client.PostAsync(
            path,
            new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json),
            TestContext.Current.CancellationToken);
    }
}
