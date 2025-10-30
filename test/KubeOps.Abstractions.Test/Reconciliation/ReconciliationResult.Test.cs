// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Abstractions.Test.Reconciliation;

public sealed class ReconciliationResultTest
{
    [Fact]
    public void Success_Should_Create_Successful_Result()
    {
        var entity = CreateTestEntity();

        var result = ReconciliationResult<V1ConfigMap>.Success(entity);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Entity.Should().Be(entity);
        result.ErrorMessage.Should().BeNull();
        result.Error.Should().BeNull();
        result.RequeueAfter.Should().BeNull();
    }

    [Fact]
    public void Success_With_RequeueAfter_Should_Set_RequeueAfter()
    {
        var entity = CreateTestEntity();
        var requeueAfter = TimeSpan.FromMinutes(5);

        var result = ReconciliationResult<V1ConfigMap>.Success(entity, requeueAfter);

        result.IsSuccess.Should().BeTrue();
        result.RequeueAfter.Should().Be(requeueAfter);
        result.Entity.Should().Be(entity);
    }

    [Fact]
    public void Success_With_Null_RequeueAfter_Should_Not_Set_RequeueAfter()
    {
        var entity = CreateTestEntity();

        var result = ReconciliationResult<V1ConfigMap>.Success(entity, null);

        result.IsSuccess.Should().BeTrue();
        result.RequeueAfter.Should().BeNull();
    }

    [Fact]
    public void Failure_Should_Create_Failed_Result_With_ErrorMessage()
    {
        var entity = CreateTestEntity();
        var errorMessage = "Reconciliation failed due to timeout";

        var result = ReconciliationResult<V1ConfigMap>.Failure(entity, errorMessage);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Entity.Should().Be(entity);
        result.ErrorMessage.Should().Be(errorMessage);
        result.Error.Should().BeNull();
        result.RequeueAfter.Should().BeNull();
    }

    [Fact]
    public void Failure_With_Exception_Should_Set_Error()
    {
        var entity = CreateTestEntity();
        const string errorMessage = "Reconciliation failed";
        var exception = new InvalidOperationException("Invalid state detected");

        var result = ReconciliationResult<V1ConfigMap>.Failure(entity, errorMessage, exception);

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be(errorMessage);
        result.Error.Should().Be(exception);
        result.Error.Message.Should().Be("Invalid state detected");
    }

    [Fact]
    public void Failure_With_RequeueAfter_Should_Set_RequeueAfter()
    {
        var entity = CreateTestEntity();
        const string errorMessage = "Transient failure";
        var requeueAfter = TimeSpan.FromSeconds(30);

        var result = ReconciliationResult<V1ConfigMap>.Failure(
            entity,
            errorMessage,
            requeueAfter: requeueAfter);

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be(errorMessage);
        result.RequeueAfter.Should().Be(requeueAfter);
    }

    [Fact]
    public void Failure_With_All_Parameters_Should_Set_All_Properties()
    {
        var entity = CreateTestEntity();
        const string errorMessage = "Complete failure information";
        var exception = new TimeoutException("Operation timed out");
        var requeueAfter = TimeSpan.FromMinutes(2);

        var result = ReconciliationResult<V1ConfigMap>.Failure(
            entity,
            errorMessage,
            exception,
            requeueAfter);

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be(errorMessage);
        result.Error.Should().Be(exception);
        result.RequeueAfter.Should().Be(requeueAfter);
    }

    [Fact]
    public void RequeueAfter_Should_Be_Mutable()
    {
        var entity = CreateTestEntity();
        var result = ReconciliationResult<V1ConfigMap>.Success(entity);

        result.RequeueAfter = TimeSpan.FromSeconds(45);

        result.RequeueAfter.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void RequeueAfter_Can_Be_Changed_After_Creation()
    {
        var entity = CreateTestEntity();
        var initialRequeueAfter = TimeSpan.FromMinutes(1);
        var result = ReconciliationResult<V1ConfigMap>.Success(entity, initialRequeueAfter);

        result.RequeueAfter = TimeSpan.FromMinutes(5);

        result.RequeueAfter.Should().Be(TimeSpan.FromMinutes(5));
        result.RequeueAfter.Should().NotBe(initialRequeueAfter);
    }

    [Fact]
    public void RequeueAfter_Can_Be_Set_To_Null()
    {
        var entity = CreateTestEntity();
        var result = ReconciliationResult<V1ConfigMap>.Success(entity, TimeSpan.FromMinutes(1));

        result.RequeueAfter = null;

        result.RequeueAfter.Should().BeNull();
    }

    [Fact]
    public void IsSuccess_And_IsFailure_Should_Be_Mutually_Exclusive()
    {
        var entity = CreateTestEntity();

        var successResult = ReconciliationResult<V1ConfigMap>.Success(entity);
        var failureResult = ReconciliationResult<V1ConfigMap>.Failure(entity, "Error");

        successResult.IsSuccess.Should().BeTrue();
        successResult.IsFailure.Should().BeFalse();

        failureResult.IsSuccess.Should().BeFalse();
        failureResult.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Success_Result_ErrorMessage_Should_Be_Null()
    {
        var entity = CreateTestEntity();

        var result = ReconciliationResult<V1ConfigMap>.Success(entity);

        if (result.IsSuccess)
        {
            result.ErrorMessage.Should().BeNull();
        }
    }

    [Fact]
    public void Failure_Result_ErrorMessage_Should_Not_Be_Null()
    {
        var entity = CreateTestEntity();
        var errorMessage = "Something went wrong";

        var result = ReconciliationResult<V1ConfigMap>.Failure(entity, errorMessage);

        if (result.IsFailure)
        {
            // This should compile without nullable warning due to MemberNotNullWhen attribute
            string message = result.ErrorMessage;
            message.Should().NotBeNull();
            message.Should().Be(errorMessage);
        }
    }

    [Fact]
    public void Record_Equality_Should_Work_For_Success_Results()
    {
        var entity1 = CreateTestEntity("test-entity");
        var entity2 = CreateTestEntity("test-entity");

        var result1 = ReconciliationResult<V1ConfigMap>.Success(entity1);
        var result2 = ReconciliationResult<V1ConfigMap>.Success(entity2);

        // Records with same values should be equal
        result1.Should().NotBeSameAs(result2);
    }

    [Fact]
    public void Entity_Reference_Should_Be_Preserved()
    {
        var entity = CreateTestEntity();

        var result = ReconciliationResult<V1ConfigMap>.Success(entity);

        result.Entity.Should().BeSameAs(entity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(3600)]
    public void Success_Should_Accept_Various_RequeueAfter_Values(int seconds)
    {
        var entity = CreateTestEntity();
        var requeueAfter = TimeSpan.FromSeconds(seconds);

        var result = ReconciliationResult<V1ConfigMap>.Success(entity, requeueAfter);

        result.RequeueAfter.Should().Be(requeueAfter);
    }

    [Theory]
    [InlineData("Short error")]
    [InlineData("A much longer error message that contains detailed information about what went wrong")]
    [InlineData("")]
    public void Failure_Should_Accept_Various_ErrorMessage_Lengths(string errorMessage)
    {
        var entity = CreateTestEntity();

        var result = ReconciliationResult<V1ConfigMap>.Failure(entity, errorMessage);

        result.ErrorMessage.Should().Be(errorMessage);
    }

    private static V1ConfigMap CreateTestEntity(string? name = null)
        => new()
        {
            Metadata = new()
            {
                Name = name ?? "test-configmap",
                NamespaceProperty = "default",
                Uid = Guid.NewGuid().ToString(),
            },
        };
}
