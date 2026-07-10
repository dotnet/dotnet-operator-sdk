// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;
using KubeOps.Transpiler.Exceptions;

namespace KubeOps.Transpiler.Test;

public sealed partial class CrdsMlcTest
{
    [Trait("Area", "SchemaValidation")]
    [Theory]
    [InlineData(typeof(XListTypeOnStringEntity), "XListTypeAttribute")]
    [InlineData(typeof(XMapTypeOnStringEntity), "XMapTypeAttribute")]
    [InlineData(typeof(XListMapKeysWithoutMapListEntity), "XListMapKeysAttribute")]
    [InlineData(typeof(XListMapKeysWithSetListEntity), "XListMapKeysAttribute")]
    [InlineData(typeof(XListMapKeysWithAtomicListEntity), "XListMapKeysAttribute")]
    [InlineData(typeof(MapListWithoutKeysEntity), "XListMapKeysAttribute")]
    [InlineData(typeof(MapListWithScalarItemsEntity), "map-list items")]
    [InlineData(typeof(MapListWithMissingKeyEntity), "must be an item property")]
    [InlineData(typeof(MapListWithDuplicateKeyEntity), "duplicated")]
    [InlineData(typeof(MapListWithObjectKeyEntity), "must be scalar")]
    [InlineData(typeof(MapListWithNullableKeyEntity), "cannot be nullable")]
    [InlineData(typeof(MapListWithOptionalKeyEntity), "must be required")]
    [InlineData(typeof(SetListWithNullableItemsEntity), "cannot be nullable")]
    [InlineData(typeof(SetListWithObjectItemsEntity), "Set lists with object")]
    [InlineData(typeof(AdditionalPrinterColumnWithObjectEntity), "Additional printer column")]
    [InlineData(typeof(GenericAdditionalPrinterColumnWithInvalidTypeEntity), "Additional printer column")]
    [InlineData(typeof(AdditionalPrinterColumnWithInvalidFormatEntity), "unsupported format")]
    [InlineData(typeof(AdditionalPrinterColumnWithEmptyNameEntity), "name is required")]
    [InlineData(typeof(GenericAdditionalPrinterColumnWithEmptyJsonPathEntity), "JSONPath is required")]
    [InlineData(typeof(ValidationRuleWithEmptyRuleEntity), "Validation rule")]
    [InlineData(typeof(ClassValidationRuleWithEmptyRuleEntity), "Validation rule")]
    [InlineData(typeof(ValidationRuleWithMultilineFieldPathEntity), "fieldPath")]
    [InlineData(typeof(ValidationRuleWithCarriageReturnFieldPathEntity), "fieldPath")]
    [InlineData(typeof(ValidationRuleWithLeadingNewlineFieldPathEntity), "fieldPath")]
    [InlineData(typeof(ValidationRuleWithWhitespaceFieldPathEntity), "fieldPath")]
    [InlineData(typeof(ValidationRuleWithMultilineMessageEntity), "message")]
    [InlineData(typeof(ValidationRuleWithCarriageReturnMessageEntity), "message")]
    [InlineData(typeof(ValidationRuleWithWhitespaceMessageEntity), "message")]
    [InlineData(typeof(ValidationRuleWithWhitespaceMessageExpressionEntity), "messageExpression")]
    [InlineData(typeof(ValidationRuleWithUnsupportedReasonEntity), "reason")]
    [InlineData(typeof(ValidationRuleWithCarriageReturnRuleWithoutMessageEntity), "line breaks")]
    [InlineData(typeof(ValidationRuleWithMultilineRuleWithoutMessageEntity), "line breaks")]
    public void Should_Reject_Invalid_Schema_Attribute_Combinations(Type type, string expectedMessage)
    {
        var act = () => _mlc.Transpile(type);

        act.Should().Throw<TranspilationFailedException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Trait("Area", "SchemaValidation")]
    [Theory]
    [InlineData(typeof(ItemsOnStringEntity))]
    [InlineData(typeof(ItemsWithNegativeLimitEntity))]
    [InlineData(typeof(ItemsWithNegativeMaxLimitEntity))]
    [InlineData(typeof(ItemsWithInvertedLimitsEntity))]
    [InlineData(typeof(LengthOnIntegerEntity))]
    [InlineData(typeof(LengthWithNegativeMinLimitEntity))]
    [InlineData(typeof(LengthWithNegativeMaxLimitEntity))]
    [InlineData(typeof(LengthWithInvertedLimitsEntity))]
    [InlineData(typeof(PatternOnIntegerEntity))]
    [InlineData(typeof(PropertyLimitsOnStringEntity))]
    [InlineData(typeof(PropertyLimitsWithNegativeMinLimitEntity))]
    [InlineData(typeof(PropertyLimitsWithNegativeMaxLimitEntity))]
    [InlineData(typeof(PropertyLimitsWithInvertedLimitsEntity))]
    [InlineData(typeof(MultipleOfOnStringEntity))]
    [InlineData(typeof(MultipleOfZeroEntity))]
    [InlineData(typeof(RangeWithInvertedLimitsEntity))]
    [InlineData(typeof(SetListWithNestedArrayItemsEntity))]
    [InlineData(typeof(ValidationRuleWithEmptyFieldPathEntity))]
    [InlineData(typeof(ValidationRuleWithEmptyMessageEntity))]
    [InlineData(typeof(ValidationRuleWithEmptyMessageExpressionEntity))]
    [InlineData(typeof(GenericAdditionalPrinterColumnWithEmptyFormatEntity))]
    [InlineData(typeof(ValidationRuleWithTrailingNewlineRuleEntity))]
    public void Should_Not_Reject_OpenApi_Validation_Keywords_That_Kubernetes_Accepts(Type type)
    {
        var act = () => _mlc.Transpile(type);

        act.Should().NotThrow();
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ItemsOnStringEntity : CustomKubernetesEntity
    {
        [Items(1, 3)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ItemsWithNegativeLimitEntity : CustomKubernetesEntity
    {
        [Items(-2)]
        public string[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ItemsWithNegativeMaxLimitEntity : CustomKubernetesEntity
    {
        [Items(maxItems: -2)]
        public string[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ItemsWithInvertedLimitsEntity : CustomKubernetesEntity
    {
        [Items(3, 1)]
        public string[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class LengthOnIntegerEntity : CustomKubernetesEntity
    {
        [Length(1, 3)]
        public int Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class LengthWithNegativeMinLimitEntity : CustomKubernetesEntity
    {
        [Length(minLength: -2)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class LengthWithNegativeMaxLimitEntity : CustomKubernetesEntity
    {
        [Length(maxLength: -2)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class LengthWithInvertedLimitsEntity : CustomKubernetesEntity
    {
        [Length(3, 1)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class PatternOnIntegerEntity : CustomKubernetesEntity
    {
        [Pattern(@"\d+")]
        public int Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class PropertyLimitsOnStringEntity : CustomKubernetesEntity
    {
        [PropertyLimits(1, 3)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class PropertyLimitsWithNegativeMinLimitEntity : CustomKubernetesEntity
    {
        [PropertyLimits(minProperties: -2)]
        public object Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class PropertyLimitsWithNegativeMaxLimitEntity : CustomKubernetesEntity
    {
        [PropertyLimits(maxProperties: -2)]
        public object Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class PropertyLimitsWithInvertedLimitsEntity : CustomKubernetesEntity
    {
        [PropertyLimits(3, 1)]
        public object Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MultipleOfOnStringEntity : CustomKubernetesEntity
    {
        [MultipleOf(2)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MultipleOfZeroEntity : CustomKubernetesEntity
    {
        [MultipleOf(0)]
        public int Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class RangeWithInvertedLimitsEntity : CustomKubernetesEntity
    {
        [RangeMinimum(10)]
        [RangeMaximum(5)]
        public int Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class XListTypeOnStringEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Set)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class XMapTypeOnStringEntity : CustomKubernetesEntity
    {
        [XMapType(XMapType.Atomic)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class XListMapKeysWithoutMapListEntity : CustomKubernetesEntity
    {
        [XListMapKeys("name")]
        public MapListItem[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class XListMapKeysWithSetListEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Set)]
        [XListMapKeys("name")]
        public MapListItem[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class XListMapKeysWithAtomicListEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Atomic)]
        [XListMapKeys("name")]
        public MapListItem[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MapListWithoutKeysEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Map)]
        public MapListItem[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MapListWithScalarItemsEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Map)]
        [XListMapKeys("name")]
        public string[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MapListWithMissingKeyEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Map)]
        [XListMapKeys("missing")]
        public MapListItem[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MapListWithDuplicateKeyEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Map)]
        [XListMapKeys("name", "name")]
        public MapListItem[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MapListWithObjectKeyEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Map)]
        [XListMapKeys("nested")]
        public MapListItemWithObjectKey[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MapListWithNullableKeyEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Map)]
        [XListMapKeys("name")]
        public MapListItemWithNullableKey[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MapListWithOptionalKeyEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Map)]
        [XListMapKeys("name")]
        public MapListItemWithOptionalKey[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class SetListWithNullableItemsEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Set)]
        public int?[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class SetListWithObjectItemsEntity : CustomKubernetesEntity
    {
        [XListType(XListType.Set)]
        public MapListItem[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class SetListWithNestedArrayItemsEntity : CustomKubernetesEntity
    {
        // Nested arrays have implicitly atomic list topology, which Kubernetes accepts as set-list items.
        [XListType(XListType.Set)]
        public List<List<string>> Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [GenericAdditionalPrinterColumn(".spec.property", "Property", "string", Format = "")]
    private sealed class GenericAdditionalPrinterColumnWithEmptyFormatEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class AdditionalPrinterColumnWithObjectEntity : CustomKubernetesEntity
    {
        [AdditionalPrinterColumn]
        public SchemaValidationNestedObject Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [GenericAdditionalPrinterColumn(".spec.property", "Property", "object")]
    private sealed class GenericAdditionalPrinterColumnWithInvalidTypeEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class AdditionalPrinterColumnWithInvalidFormatEntity : CustomKubernetesEntity
    {
        [AdditionalPrinterColumn]
        public Guid Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class AdditionalPrinterColumnWithEmptyNameEntity : CustomKubernetesEntity
    {
        [AdditionalPrinterColumn(name: "")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [GenericAdditionalPrinterColumn("", "Property", "string")]
    private sealed class GenericAdditionalPrinterColumnWithEmptyJsonPathEntity : CustomKubernetesEntity;

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithEmptyRuleEntity : CustomKubernetesEntity
    {
        [ValidationRule("")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [ValidationRule("")]
    private sealed class ClassValidationRuleWithEmptyRuleEntity : CustomKubernetesEntity;

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithEmptyFieldPathEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", fieldPath: "")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithMultilineFieldPathEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", fieldPath: "spec\nproperty")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithEmptyMessageEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", message: "")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithMultilineMessageEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", message: "line 1\nline 2")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithCarriageReturnMessageEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", message: "line 1\rline 2")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithWhitespaceMessageEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", message: "   ")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithWhitespaceFieldPathEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", fieldPath: "   ")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithCarriageReturnFieldPathEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", fieldPath: "spec\rproperty")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithLeadingNewlineFieldPathEntity : CustomKubernetesEntity
    {
        // Kubernetes checks the original fieldPath, so a leading line break is rejected even though it trims away.
        [ValidationRule("self != ''", fieldPath: "\nspec")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithWhitespaceMessageExpressionEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", messageExpression: "   ")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithCarriageReturnRuleWithoutMessageEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''\r&& self.size() > 1")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithTrailingNewlineRuleEntity : CustomKubernetesEntity
    {
        // Kubernetes checks the trimmed rule, so a trailing line break alone does not require a message.
        [ValidationRule("self != ''\n")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithEmptyMessageExpressionEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", messageExpression: "")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithUnsupportedReasonEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''", reason: "Unsupported")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ValidationRuleWithMultilineRuleWithoutMessageEntity : CustomKubernetesEntity
    {
        [ValidationRule("self != ''\n&& self.size() > 1")]
        public string Property { get; set; } = null!;
    }

    private sealed class MapListItem
    {
        [Required]
        public string Name { get; set; } = null!;
    }

    private sealed class MapListItemWithObjectKey
    {
        [Required]
        public SchemaValidationNestedObject Nested { get; set; } = null!;
    }

    private sealed class MapListItemWithNullableKey
    {
        [Required]
        public string? Name { get; set; }
    }

    private sealed class MapListItemWithOptionalKey
    {
        public string Name { get; set; } = null!;
    }

    private sealed class SchemaValidationNestedObject
    {
        public string Value { get; set; } = null!;
    }
}
