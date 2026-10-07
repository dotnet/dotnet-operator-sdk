// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Operator.Retry;

namespace KubeOps.Operator.Test.Retry;

[Trait("Area", "Retry")]
public sealed class KubernetesApiErrorsTest
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.Conflict, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void IsTransient_Should_Classify_Status_Code_Of_Both_Exception_Types(
        HttpStatusCode statusCode,
        bool expected)
    {
        KubernetesApiErrors.IsTransient(ApiErrors.Http(statusCode)).Should().Be(expected);
        KubernetesApiErrors.IsTransient(new KubernetesException(new V1Status { Code = (int)statusCode }))
            .Should().Be(expected);
    }

    [Fact]
    public void IsTransient_Should_Treat_Network_Errors_As_Transient()
    {
        KubernetesApiErrors.IsTransient(new HttpRequestException("unreachable")).Should().BeTrue();
        KubernetesApiErrors.IsTransient(new TimeoutException()).Should().BeTrue();
        KubernetesApiErrors.IsTransient(new TaskCanceledException()).Should().BeTrue();
        KubernetesApiErrors.IsTransient(new KubernetesException(new V1Status())).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_Should_Treat_Unknown_Errors_As_Permanent()
        => KubernetesApiErrors.IsTransient(new InvalidOperationException()).Should().BeFalse();

    [Fact]
    public void GetRetryAfter_Should_Read_Header_Of_Http_Operation_Exception()
        => KubernetesApiErrors
            .GetRetryAfter(ApiErrors.Http(HttpStatusCode.TooManyRequests, retryAfterSeconds: 3))
            .Should().Be(TimeSpan.FromSeconds(3));

    [Fact]
    public void GetRetryAfter_Should_Read_Status_Details_Of_Kubernetes_Exception()
        => KubernetesApiErrors
            .GetRetryAfter(new KubernetesException(new V1Status
            {
                Code = (int)HttpStatusCode.TooManyRequests,
                Details = new V1StatusDetails { RetryAfterSeconds = 2 },
            }))
            .Should().Be(TimeSpan.FromSeconds(2));

    [Fact]
    public void GetRetryAfter_Should_Return_Null_Without_Server_Hint()
        => KubernetesApiErrors
            .GetRetryAfter(ApiErrors.Http(HttpStatusCode.TooManyRequests))
            .Should().BeNull();

    [Fact]
    public void GetErrorType_Should_Prefer_Status_Code_Over_Exception_Type()
    {
        KubernetesApiErrors.GetErrorType(ApiErrors.Http(HttpStatusCode.TooManyRequests))
            .Should().Be("429");
        KubernetesApiErrors.GetErrorType(new HttpRequestException())
            .Should().Be("System.Net.Http.HttpRequestException");
    }
}
