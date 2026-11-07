// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;

using FluentAssertions;

using KubeOps.Abstractions.Entities;
using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks.Admission;
using KubeOps.Operator.Web.Webhooks.Admission.Mutation;

using Microsoft.AspNetCore.Http;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission.Mutation;

[Trait("Area", "AdmissionResult")]
[RequiresPreviewFeatures]
public sealed class MutationResultTest
{
    [Fact]
    public async Task Should_Fail_Without_Request_Uid()
    {
        var (statusCode, body) = await ActionResultExecutor.ExecuteAsync(
            new MutationResult<V1OperatorWebIntegrationTestEntity>());

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        body.Should().Be("No request UID was provided.");
    }

    [Fact]
    public async Task Should_Fail_When_A_Modified_Object_Has_No_Original()
    {
        var result = new MutationResult<V1OperatorWebIntegrationTestEntity>(new("test", "user"))
        {
            Uid = "request-uid",
        };

        var (statusCode, body) = await ActionResultExecutor.ExecuteAsync(result);

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        body.Should().Be("No original object was provided.");
    }

    [Fact]
    public async Task Should_Write_A_Json_Patch_For_A_Modified_Object()
    {
        var original = new V1OperatorWebIntegrationTestEntity("test", "user");
        var result = new MutationResult<V1OperatorWebIntegrationTestEntity>(new("test", "overwritten"))
        {
            Uid = "request-uid",
            OriginalObject = original.ToNode(),
        };

        var (statusCode, body) = await ActionResultExecutor.ExecuteAsync(result);

        statusCode.Should().Be(StatusCodes.Status200OK);
        var response = JsonNode.Parse(body)!["response"]!;
        response["allowed"]!.GetValue<bool>().Should().BeTrue();
        response["patchType"]!.GetValue<string>().Should().Be("JSONPatch");
        var patch = JsonNode.Parse(Encoding.UTF8.GetString(
            Convert.FromBase64String(response["patch"]!.GetValue<string>())))!.AsArray();
        patch.Should().ContainSingle();
        patch[0]!["op"]!.GetValue<string>().Should().Be("replace");
        patch[0]!["path"]!.GetValue<string>().Should().Be("/spec/username");
        patch[0]!["value"]!.GetValue<string>().Should().Be("overwritten");
    }

    [Fact]
    public async Task Should_Write_No_Patch_Without_Changes()
    {
        var result = new MutationResult<V1OperatorWebIntegrationTestEntity> { Uid = "request-uid" };

        var (_, body) = await ActionResultExecutor.ExecuteAsync(result);

        var response = JsonNode.Parse(body)!["response"]!;
        response["allowed"]!.GetValue<bool>().Should().BeTrue();
        response["patch"].Should().BeNull();
        response["patchType"].Should().BeNull();
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
                OldObject = new("test", "user"),
            },
        };

        var result = await new TestMutationWebhook().Mutate(request, TestContext.Current.CancellationToken);

        var mutation = result.Should().BeOfType<MutationResult<V1OperatorWebIntegrationTestEntity>>().Subject;
        mutation.Valid.Should().BeFalse();
        mutation.Uid.Should().Be("request-uid");
        mutation.Status.Should().Be(
            new AdmissionStatus("Operation CONNECT is not supported.", StatusCodes.Status422UnprocessableEntity));
    }
}
