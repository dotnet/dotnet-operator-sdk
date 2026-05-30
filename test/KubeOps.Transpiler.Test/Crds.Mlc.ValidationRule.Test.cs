// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace KubeOps.Transpiler.Test;

public sealed partial class CrdsMlcTest
{
    private const string Rule1 = "has(self.https) || self.kind != 'https'";
    private const string Message1 = "https object must be specified if kind is https";
    private const string FieldPath1 = ".property";
    private const string Reason1 = "reason";
    private const string MessageExpression1 = "\"https object must be specified if kind is \" + string(self.kind)";

    private const string Rule2 = "has(self.workflow) || self.kind != 'my-workflow";
    private const string Message2 = "workflow must be specified if handling is workflow";

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Set_Validations()
    {
        var crd = _mlc.Transpile(typeof(SingleValidateAttrEntity));

        var specProperties = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.XKubernetesValidations.Should().HaveCount(1);
        specProperties.XKubernetesValidations[0].Rule.Should().Be(Rule1);
        specProperties.XKubernetesValidations[0].Message.Should().Be(Message1);
        specProperties.XKubernetesValidations[0].MessageExpression.Should().BeNull();
        specProperties.XKubernetesValidations[0].FieldPath.Should().BeNull();
        specProperties.XKubernetesValidations[0].Reason.Should().BeNull();
    }

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Set_MultipleValidations()
    {
        var crd = _mlc.Transpile(typeof(MultiValidateAttrEntity));

        var specProperties = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.XKubernetesValidations.Should().HaveCount(2);
        specProperties.XKubernetesValidations[0].Rule.Should().Be(Rule1);
        specProperties.XKubernetesValidations[0].Message.Should().Be(Message1);
        specProperties.XKubernetesValidations[0].MessageExpression.Should().BeNull();
        specProperties.XKubernetesValidations[0].FieldPath.Should().BeNull();
        specProperties.XKubernetesValidations[0].Reason.Should().BeNull();
        specProperties.XKubernetesValidations[1].Rule.Should().Be(Rule2);
        specProperties.XKubernetesValidations[1].Message.Should().Be(Message2);
        specProperties.XKubernetesValidations[1].MessageExpression.Should().BeNull();
        specProperties.XKubernetesValidations[1].FieldPath.Should().BeNull();
        specProperties.XKubernetesValidations[1].Reason.Should().BeNull();
    }

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Omit_Validations()
    {
        var crd = _mlc.Transpile(typeof(NoValidateAttrEntity));

        var specProperties = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.XKubernetesValidations.Should().BeNull();
    }

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Set_Validations_On_Root_Class()
    {
        var crd = _mlc.Transpile(typeof(RootClassValidateAttrEntity));

        var schema = crd.Spec.Versions[0].Schema.OpenAPIV3Schema;
        schema.XKubernetesValidations.Should().HaveCount(1);
        schema.XKubernetesValidations[0].Rule.Should().Be(Rule1);
        schema.XKubernetesValidations[0].Message.Should().Be(Message1);
        schema.XKubernetesValidations[0].MessageExpression.Should().BeNull();
        schema.XKubernetesValidations[0].FieldPath.Should().BeNull();
        schema.XKubernetesValidations[0].Reason.Should().BeNull();
    }

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Set_Multiple_Validations_On_Root_Class()
    {
        var crd = _mlc.Transpile(typeof(MultiRootClassValidateAttrEntity));

        var schema = crd.Spec.Versions[0].Schema.OpenAPIV3Schema;
        schema.XKubernetesValidations.Should().HaveCount(2);
        schema.XKubernetesValidations[0].Rule.Should().Be(Rule1);
        schema.XKubernetesValidations[1].Rule.Should().Be(Rule2);
    }

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Set_Validations_On_Nested_Class()
    {
        var crd = _mlc.Transpile(typeof(NestedClassValidateAttrEntity));

        var nestedSchema = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["nested"];
        nestedSchema.XKubernetesValidations.Should().HaveCount(1);
        nestedSchema.XKubernetesValidations[0].Rule.Should().Be(Rule1);
        nestedSchema.XKubernetesValidations[0].Message.Should().Be(Message1);
    }

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Merge_Class_And_Property_Validations()
    {
        var crd = _mlc.Transpile(typeof(ClassAndPropertyValidateAttrEntity));

        var nestedSchema = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["nested"];
        nestedSchema.XKubernetesValidations.Should().HaveCount(2);
        nestedSchema.XKubernetesValidations[0].Rule.Should().Be(Rule1);
        nestedSchema.XKubernetesValidations[1].Rule.Should().Be(Rule2);
    }

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Inherit_And_Merge_Class_Validations_From_Base_Entity()
    {
        var crd = _mlc.Transpile(typeof(DerivedClassValidateAttrEntity));

        var schema = crd.Spec.Versions[0].Schema.OpenAPIV3Schema;
        schema.XKubernetesValidations.Should().HaveCount(2);
        schema.XKubernetesValidations.Select(v => v.Rule).Should().Contain([Rule1, Rule2]);
    }

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Inherit_And_Merge_Class_Validations_On_Nested_Class()
    {
        var crd = _mlc.Transpile(typeof(NestedDerivedClassValidateAttrEntity));

        var nestedSchema = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["nested"];
        nestedSchema.XKubernetesValidations.Should().HaveCount(2);
        nestedSchema.XKubernetesValidations.Select(v => v.Rule).Should().Contain([Rule1, Rule2]);
    }

    [Trait("Area", "ValidationRule")]
    [Fact]
    public void Should_Set_ValidationFields()
    {
        var crd = _mlc.Transpile(typeof(AllFieldsValidateAttrEntity));

        var specProperties = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.XKubernetesValidations.Should().HaveCount(1);
        specProperties.XKubernetesValidations[0].Rule.Should().Be(Rule1);
        specProperties.XKubernetesValidations[0].Message.Should().Be(Message1);
        specProperties.XKubernetesValidations[0].MessageExpression.Should().Be(MessageExpression1);
        specProperties.XKubernetesValidations[0].FieldPath.Should().Be(FieldPath1);
        specProperties.XKubernetesValidations[0].Reason.Should().Be(Reason1);
    }

    #region Test Entity Classes

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public sealed class NoValidateAttrEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [ValidationRule(Rule1, message: Message1)]
    public sealed class RootClassValidateAttrEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [ValidationRule(Rule1, message: Message1)]
    [ValidationRule(Rule2, message: Message2)]
    public sealed class MultiRootClassValidateAttrEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public sealed class NestedClassValidateAttrEntity : CustomKubernetesEntity
    {
        public NestedObject Nested { get; set; } = new();

        [ValidationRule(Rule1, message: Message1)]
        public sealed class NestedObject
        {
            public string Name { get; set; } = null!;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public sealed class ClassAndPropertyValidateAttrEntity : CustomKubernetesEntity
    {
        [ValidationRule(Rule2, message: Message2)]
        public NestedWithClassRule Nested { get; set; } = new();

        [ValidationRule(Rule1, message: Message1)]
        public sealed class NestedWithClassRule
        {
            public string Name { get; set; } = null!;
        }
    }

    [ValidationRule(Rule1, message: Message1)]
    public abstract class BaseValidateAttrEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [ValidationRule(Rule2, message: Message2)]
    public sealed class DerivedClassValidateAttrEntity : BaseValidateAttrEntity
    {
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public sealed class NestedDerivedClassValidateAttrEntity : CustomKubernetesEntity
    {
        public DerivedNestedObject Nested { get; set; } = new();

        [ValidationRule(Rule1, message: Message1)]
        public abstract class BaseNestedObject
        {
            public string Name { get; set; } = null!;
        }

        [ValidationRule(Rule2, message: Message2)]
        public sealed class DerivedNestedObject : BaseNestedObject
        {
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public sealed class SingleValidateAttrEntity : CustomKubernetesEntity
    {
        [ValidationRule(Rule1, message: Message1)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public sealed class MultiValidateAttrEntity : CustomKubernetesEntity
    {
        [ValidationRule(Rule1, message: Message1)]
        [ValidationRule(Rule2, message: Message2)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public sealed class AllFieldsValidateAttrEntity : CustomKubernetesEntity
    {
        [ValidationRule(Rule1, FieldPath1, Message1, MessageExpression1, Reason1)]
        public string Property { get; set; } = null!;
    }

    #endregion Test Entity Classes
}
