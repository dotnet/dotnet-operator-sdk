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
        var sut = new EntityWithGroupAsStringValueFinalizer();
        var entity = new EntityWithGroupAsStringValue();

        var identifierName = sut.GetIdentifierName(entity);

        identifierName.Should().Be("finalizer.test/entitywithgroupasstringvaluefinalizer");
    }

    [Fact]
    public void GetIdentifierName_Should_Return_Correct_Name_When_Entity_Group_Has_Const_Value()
    {
        var sut = new EntityWithGroupAsConstValueFinalizer();
        var entity = new EntityWithGroupAsConstValue();

        var identifierName = sut.GetIdentifierName(entity);

        identifierName.Should().Be($"{Group}/entitywithgroupasconstvaluefinalizer");
    }

    [Fact]
    public void GetIdentifierName_Should_Return_Correct_Name_When_Entity_Group_Has_No_Value()
    {
        var sut = new EntityWithNoGroupFinalizer();
        var entity = new EntityWithNoGroupValue();

        var identifierName = sut.GetIdentifierName(entity);

        identifierName.Should().Be("entitywithnogroupfinalizer");
    }

    [Fact]
    public void GetIdentifierName_Should_Return_Correct_Name_When_Finalizer_Not_Ending_With_Finalizer()
    {
        var sut = new EntityFinalizerNotEndingOnFinalizer1();
        var entity = new EntityWithGroupAsConstValue();

        var identifierName = sut.GetIdentifierName(entity);

        identifierName.Should().Be($"{Group}/entityfinalizernotendingonfinalizer1finalizer");
    }

    [Fact]
    public void GetIdentifierName_Should_Return_Correct_Name_When_Finalizer_Identifier_Would_Be_Greater_Than_63_Characters()
    {
        var sut = new EntityFinalizerWithATotalIdentifierNameHavingALengthGreaterThan63();
        var entity = new EntityWithGroupAsConstValue();

        var identifierName = sut.GetIdentifierName(entity);

        identifierName.Should().Be($"{Group}/entityfinalizerwithatotalidentifiernamehavingale");
        identifierName.Length.Should().Be(63);
    }

    private sealed class EntityFinalizerWithATotalIdentifierNameHavingALengthGreaterThan63
        : IEntityFinalizer<EntityWithGroupAsConstValue>
    {
        public Task<Result<EntityWithGroupAsConstValue>> FinalizeAsync(EntityWithGroupAsConstValue entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<EntityWithGroupAsConstValue>.ForSuccess(entity));
    }

    private sealed class EntityFinalizerNotEndingOnFinalizer1
        : IEntityFinalizer<EntityWithGroupAsConstValue>
    {
        public Task<Result<EntityWithGroupAsConstValue>> FinalizeAsync(EntityWithGroupAsConstValue entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<EntityWithGroupAsConstValue>.ForSuccess(entity));
    }

    private sealed class EntityWithGroupAsStringValueFinalizer
        : IEntityFinalizer<EntityWithGroupAsStringValue>
    {
        public Task<Result<EntityWithGroupAsStringValue>> FinalizeAsync(EntityWithGroupAsStringValue entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<EntityWithGroupAsStringValue>.ForSuccess(entity));
    }

    private sealed class EntityWithGroupAsConstValueFinalizer
        : IEntityFinalizer<EntityWithGroupAsConstValue>
    {
        public Task<Result<EntityWithGroupAsConstValue>> FinalizeAsync(EntityWithGroupAsConstValue entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<EntityWithGroupAsConstValue>.ForSuccess(entity));
    }

    private sealed class EntityWithNoGroupFinalizer
        : IEntityFinalizer<EntityWithNoGroupValue>
    {
        public Task<Result<EntityWithNoGroupValue>> FinalizeAsync(EntityWithNoGroupValue entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<EntityWithNoGroupValue>.ForSuccess(entity));
    }

    [KubernetesEntity(Group = "finalizer.test", ApiVersion = "v1", Kind = "FinalizerTest")]
    private sealed class EntityWithGroupAsStringValue
        : IKubernetesObject<V1ObjectMeta>
    {
        public string ApiVersion { get; set; } = "finalizer.test/v1";

        public string Kind { get; set; } = "FinalizerTest";

        public V1ObjectMeta Metadata { get; set; } = new();
    }

    [KubernetesEntity(Group = Group, ApiVersion = "v1", Kind = "FinalizerTest")]
    private sealed class EntityWithGroupAsConstValue
        : IKubernetesObject<V1ObjectMeta>
    {
        public string ApiVersion { get; set; } = "finalizer.test/v1";

        public string Kind { get; set; } = "FinalizerTest";

        public V1ObjectMeta Metadata { get; set; } = new();
    }

    [KubernetesEntity]
    private sealed class EntityWithNoGroupValue
        : IKubernetesObject<V1ObjectMeta>
    {
        public string ApiVersion { get; set; } = "finalizer.test/v1";

        public string Kind { get; set; } = "FinalizerTest";

        public V1ObjectMeta Metadata { get; set; } = new();
    }
}
