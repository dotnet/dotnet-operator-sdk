// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;

using FluentAssertions;

using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks.Admission;
using KubeOps.Operator.Web.Webhooks.Admission.Validation;

using Microsoft.AspNetCore.Http;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission.Validation;

[Trait("Area", "AdmissionResult")]
public sealed class ValidationResultTest
{
    [Fact]
    public async Task Should_Fail_Without_Request_Uid()
    {
        var (statusCode, body) = await ActionResultExecutor.ExecuteAsync(new ValidationResult());

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        body.Should().Be("No request UID was provided.");
    }

    [Fact]
    public async Task Should_Write_Denial_With_Status_And_Warnings()
    {
        var result = new ValidationResult(false)
        {
            Uid = "request-uid",
            Status = new("name may not be 'forbidden'.", StatusCodes.Status422UnprocessableEntity),
            Warnings = ["first warning"],
        };

        var (statusCode, body) = await ActionResultExecutor.ExecuteAsync(result);

        statusCode.Should().Be(StatusCodes.Status200OK);
        var response = JsonNode.Parse(body)!["response"]!;
        response["uid"]!.GetValue<string>().Should().Be("request-uid");
        response["allowed"]!.GetValue<bool>().Should().BeFalse();
        response["status"]!["message"]!.GetValue<string>().Should().Be("name may not be 'forbidden'.");
        response["status"]!["code"]!.GetValue<int>().Should().Be(StatusCodes.Status422UnprocessableEntity);
        response["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().Equal("first warning");
    }

    [Fact]
    public async Task Should_Deny_Unsupported_Operations()
    {
        var request = new AdmissionRequest<V1OperatorWebIntegrationTestEntity>
        {
            Request = new()
            {
                Uid = "request-uid",
                Operation = "CONNECT",
                Object = new("test", "user"),
            },
        };

        var result = await new TestValidationWebhook().Validate(request, TestContext.Current.CancellationToken);

        var validation = result.Should().BeOfType<ValidationResult>().Subject;
        validation.Valid.Should().BeFalse();
        validation.Uid.Should().Be("request-uid");
        validation.Status.Should().Be(
            new AdmissionStatus("Operation CONNECT is not supported.", StatusCodes.Status422UnprocessableEntity));
    }
}
