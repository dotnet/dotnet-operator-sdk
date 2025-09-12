// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Finalizer;

namespace KubeOps.Abstractions.Test.Finalizer;

public sealed class EntityFinalizerExtensions
{
    private const string Group = "finalizer.test";

    [Fact]
    public void GetIdentifierName_Should_Return_Correct_Name_When_Entity_Group_Has_String_Value()
    {
        var sut = new EntityWithStringValueFinalizer();
        var entity = new EntityFinalizerTestEntityWithStringValue();

        var identifierName = sut.GetIdentifierName(entity);

        identifierName.Should().Be("finalizer.test/entitywithstringvaluefinalizer");
    }

    [Fact]
    public void GetIdentifierName_Should_Return_Correct_Name_When_Entity_Group_Has_Const_Value()
    {
        var sut = new EntityWithConstValueFinalizer();
        var entity = new EntityFinalizerTestEntityWithConstValue();

        var identifierName = sut.GetIdentifierName(entity);

        identifierName.Should().Be($"{Group}/entitywithconstvaluefinalizer");
    }

    [Fact]
    public void GetIdentifierName_Should_Return_Correct_Name_When_Finalizer_Not_Ending_With_Finalizer()
    {
        var sut = new EntityFinalizerNotEndingOnFinalizerTest();
        var entity = new EntityFinalizerTestEntityWithConstValue();

        var identifierName = sut.GetIdentifierName(entity);

        identifierName.Should().Be($"{Group}/entityfinalizernotendingonfinalizertestfinalizer");
    }

    [Fact]
    public void GetIdentifierName_Should_Return_Correct_Name_When_Finalizer_Identifier_Would_Be_Greater_Than_63_Characters()
    {
        var sut = new EntityFinalizerWithATotalIdentifierNameHavingALengthGreaterThan63();
        var entity = new EntityFinalizerTestEntityWithConstValue();

        var identifierName = sut.GetIdentifierName(entity);

        identifierName.Should().Be($"{Group}/entityfinalizerwithatotalidentifiernamehavingale");
        identifierName.Length.Should().Be(63);
    }

    private sealed class EntityFinalizerWithATotalIdentifierNameHavingALengthGreaterThan63
        : IEntityFinalizer<EntityFinalizerTestEntityWithConstValue>
    {
        public Task<Result<EntityFinalizerTestEntityWithConstValue>> FinalizeAsync(EntityFinalizerTestEntityWithConstValue entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<EntityFinalizerTestEntityWithConstValue>.ForSuccess(entity));
    }

    private sealed class EntityFinalizerNotEndingOnFinalizerTest
        : IEntityFinalizer<EntityFinalizerTestEntityWithConstValue>
    {
        public Task<Result<EntityFinalizerTestEntityWithConstValue>> FinalizeAsync(EntityFinalizerTestEntityWithConstValue entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<EntityFinalizerTestEntityWithConstValue>.ForSuccess(entity));
    }

    private sealed class EntityWithStringValueFinalizer
        : IEntityFinalizer<EntityFinalizerTestEntityWithStringValue>
    {
        public Task<Result<EntityFinalizerTestEntityWithStringValue>> FinalizeAsync(EntityFinalizerTestEntityWithStringValue entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<EntityFinalizerTestEntityWithStringValue>.ForSuccess(entity));
    }

    private sealed class EntityWithConstValueFinalizer
        : IEntityFinalizer<EntityFinalizerTestEntityWithConstValue>
    {
        public Task<Result<EntityFinalizerTestEntityWithConstValue>> FinalizeAsync(EntityFinalizerTestEntityWithConstValue entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<EntityFinalizerTestEntityWithConstValue>.ForSuccess(entity));
    }

    [KubernetesEntity(Group = "finalizer.test", ApiVersion = "v1", Kind = "FinalizerTest")]
    private sealed class EntityFinalizerTestEntityWithStringValue
        : IKubernetesObject<V1ObjectMeta>
    {
        public string ApiVersion { get; set; } = "finalizer.test/v1";

        public string Kind { get; set; } = "FinalizerTest";

        public V1ObjectMeta Metadata { get; set; } = new();
    }

    [KubernetesEntity(Group = Group, ApiVersion = "v1", Kind = "FinalizerTest")]
    private sealed class EntityFinalizerTestEntityWithConstValue
        : IKubernetesObject<V1ObjectMeta>
    {
        public string ApiVersion { get; set; } = "finalizer.test/v1";

        public string Kind { get; set; } = "FinalizerTest";

        public V1ObjectMeta Metadata { get; set; } = new();
    }
}
