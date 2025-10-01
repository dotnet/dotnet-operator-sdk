// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace KubeOps.Transpiler.Test;

public partial class CrdsMlcTest(MlcProvider provider) : TranspilerTestBase(provider)
{
    [Theory]
    [InlineData(typeof(StringTestEntity), "string", null, null)]
    [InlineData(typeof(NullableStringTestEntity), "string", null, true)]
    [InlineData(typeof(IntTestEntity), "integer", "int32", null)]
    [InlineData(typeof(NullableIntTestEntity), "integer", "int32", true)]
    [InlineData(typeof(LongTestEntity), "integer", "int64", null)]
    [InlineData(typeof(NullableLongTestEntity), "integer", "int64", true)]
    [InlineData(typeof(FloatTestEntity), "number", "float", null)]
    [InlineData(typeof(NullableFloatTestEntity), "number", "float", true)]
    [InlineData(typeof(DecimalTestEntity), "number", "decimal", null)]
    [InlineData(typeof(NullableDecimalTestEntity), "number", "decimal", true)]
    [InlineData(typeof(DoubleTestEntity), "number", "double", null)]
    [InlineData(typeof(NullableDoubleTestEntity), "number", "double", true)]
    [InlineData(typeof(BoolTestEntity), "boolean", null, null)]
    [InlineData(typeof(NullableBoolTestEntity), "boolean", null, true)]
    [InlineData(typeof(DateTimeTestEntity), "string", "date-time", null)]
    [InlineData(typeof(NullableDateTimeTestEntity), "string", "date-time", true)]
    [InlineData(typeof(DateTimeOffsetTestEntity), "string", "date-time", null)]
    [InlineData(typeof(NullableDateTimeOffsetTestEntity), "string", "date-time", true)]
    [InlineData(typeof(V1ObjectMetaTestEntity), "object", null, null)]
    [InlineData(typeof(ResourceQuantityTestEntity), "string", null, null)]
    [InlineData(typeof(StringArrayEntity), "array", null, null)]
    [InlineData(typeof(NullableStringArrayEntity), "array", null, true)]
    [InlineData(typeof(EnumerableIntEntity), "array", null, null)]
    [InlineData(typeof(HashSetIntEntity), "array", null, null)]
    [InlineData(typeof(SetIntEntity), "array", null, null)]
    [InlineData(typeof(InheritedEnumerableEntity), "array", null, null)]
    [InlineData(typeof(EnumEntity), "string", null, null)]
    [InlineData(typeof(NamedEnumEntity), "string", null, null)]
    [InlineData(typeof(NullableEnumEntity), "string", null, true)]
    [InlineData(typeof(DictionaryEntity), "object", null, null)]
    [InlineData(typeof(EnumerableKeyPairsEntity), "object", null, null)]
    [InlineData(typeof(IntstrOrStringEntity), null, null, null)]
    [InlineData(typeof(EmbeddedResourceEntity), "object", null, null)]
    [InlineData(typeof(EmbeddedCustomResourceEntity), "object", null, null)]
    [InlineData(typeof(EmbeddedCustomResourceGenericEntity), "object", null, null)]
    [InlineData(typeof(EmbeddedResourceListEntity), "array", null, null)]
    public void Should_Transpile_Entity_Type_Correctly(Type type, string? expectedType, string? expectedFormat,
        bool? isNullable)
    {
        var crd = _mlc.Transpile(type);
        var prop = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        prop.Type.Should().Be(expectedType);
        prop.Format.Should().Be(expectedFormat);
        prop.Nullable.Should().Be(isNullable);
    }

    [Theory]
    [InlineData(typeof(StringArrayEntity), "string", null)]
    [InlineData(typeof(NullableStringArrayEntity), "string", null)]
    [InlineData(typeof(EnumerableIntEntity), "integer", null)]
    [InlineData(typeof(EnumerableNullableIntEntity), "integer", true)]
    [InlineData(typeof(HashSetIntEntity), "integer", null)]
    [InlineData(typeof(SetIntEntity), "integer", null)]
    [InlineData(typeof(InheritedEnumerableEntity), "integer", null)]
    [InlineData(typeof(EmbeddedResourceListEntity), "object", null)]
    public void Should_Set_Correct_Array_Type(Type type, string expectedType, bool? isNullable)
    {
        var crd = _mlc.Transpile(type);
        var prop = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"].Items as V1JSONSchemaProps;
        prop!.Type.Should().Be(expectedType);
        prop.Nullable.Should().Be(isNullable);
    }

    [Theory]
    [InlineData(typeof(DictionaryEntity), "string", null)]
    [InlineData(typeof(EnumerableKeyPairsEntity), "string", null)]
    public void Should_Set_Correct_Dictionary_Additional_Properties_Type(Type type, string expectedType, bool? isNullable)
    {
        var crd = _mlc.Transpile(type);
        var prop = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"].AdditionalProperties as V1JSONSchemaProps;
        prop!.Type.Should().Be(expectedType);
        prop.Nullable.Should().Be(isNullable);
    }

    [Fact]
    public void Should_Ignore_Entity()
    {
        var crds = _mlc.Transpile(new[] { typeof(IgnoredEntity) });
        crds.Count().Should().Be(0);
    }

    [Fact]
    public void Should_Ignore_NonEntity()
    {
        var crds = _mlc.Transpile(new[] { typeof(NonEntity) });
        crds.Count().Should().Be(0);
    }

    [Fact]
    public void Should_Ignore_Kubernetes_Entities()
    {
        var crds = _mlc.Transpile(new[] { typeof(V1Pod) });
        crds.Count().Should().Be(0);
    }

    [Fact]
    public void Should_Set_Highest_Version_As_Storage()
    {
        var crds = _mlc.Transpile(new[]
        {
            typeof(V1Alpha1VersionedEntity), typeof(V1Beta1VersionedEntity), typeof(V2Beta2VersionedEntity),
            typeof(V2VersionedEntity), typeof(V1VersionedEntity), typeof(V1AttributeVersionedEntity),
            typeof(V2AttributeVersionedEntity),
        });
        var crd = crds.First(c => c.Spec.Names.Kind == "VersionedEntity");
        crd.Spec.Versions.Count(v => v.Storage).Should().Be(1);
        crd.Spec.Versions.First(v => v.Storage).Name.Should().Be("v2");
    }

    [Fact]
    public void Should_Set_Storage_When_Attribute_Is_Set()
    {
        var crds = _mlc.Transpile(new[]
        {
            typeof(V1Alpha1VersionedEntity), typeof(V1Beta1VersionedEntity), typeof(V2Beta2VersionedEntity),
            typeof(V2VersionedEntity), typeof(V1VersionedEntity), typeof(V1AttributeVersionedEntity),
            typeof(V2AttributeVersionedEntity),
        });
        var crd = crds.First(c => c.Spec.Names.Kind == "AttributeVersionedEntity");
        crd.Spec.Versions.Count(v => v.Storage).Should().Be(1);
        crd.Spec.Versions.First(v => v.Storage).Name.Should().Be("v1");
    }

    [Fact]
    public void Should_Add_Multiple_Versions_To_Crd()
    {
        var crds = _mlc.Transpile(new[]
        {
            typeof(V1Alpha1VersionedEntity), typeof(V1Beta1VersionedEntity), typeof(V2Beta2VersionedEntity),
            typeof(V2VersionedEntity), typeof(V1VersionedEntity), typeof(V1AttributeVersionedEntity),
            typeof(V2AttributeVersionedEntity),
        }).ToList();
        crds
            .First(c => c.Spec.Names.Kind == "VersionedEntity")
            .Spec.Versions.Should()
            .HaveCount(5);
        crds
            .First(c => c.Spec.Names.Kind == "AttributeVersionedEntity")
            .Spec.Versions.Should()
            .HaveCount(2);
    }

    [Fact]
    public void Should_Use_Correct_CRD()
    {
        var crd = _mlc.Transpile(typeof(Entity));
        var (ced, scope) = _mlc.ToEntityMetadata(typeof(Entity));

        crd.Kind.Should().Be(V1CustomResourceDefinition.KubeKind);
        crd.Metadata.Name.Should().Be($"{ced.PluralName}.{ced.Group}");
        crd.Spec.Names.Kind.Should().Be(ced.Kind);
        crd.Spec.Names.ListKind.Should().Be(ced.ListKind);
        crd.Spec.Names.Singular.Should().Be(ced.SingularName);
        crd.Spec.Names.Plural.Should().Be(ced.PluralName);
        crd.Spec.Scope.Should().Be(scope);
    }

    [Fact]
    public void Should_Not_Add_Status_SubResource_If_Absent()
    {
        var crd = _mlc.Transpile(typeof(Entity));
        crd.Spec.Versions.First().Subresources?.Status?.Should().BeNull();
    }

    [Fact]
    public void Should_Add_Status_SubResource_If_Present()
    {
        var crd = _mlc.Transpile(typeof(EntityWithStatus));
        crd.Spec.Versions.First().Subresources.Status.Should().NotBeNull();
    }

    [Fact]
    public void Should_Add_ShortNames_To_Crd()
    {
        var crd = _mlc.Transpile(typeof(ShortnamesEntity));
        crd.Spec.Names.ShortNames.Should()
            .NotBeNull()
            .And
            .Contain(new[] { "foo", "bar", "baz" });
    }

    [Fact]
    public void Should_Set_Description_On_Class()
    {
        var crd = _mlc.Transpile(typeof(ClassDescriptionAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["spec"];
        specProperties.Description.Should().NotBe("");
    }

    [Fact]
    public void Should_Set_Description()
    {
        var crd = _mlc.Transpile(typeof(DescriptionAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.Description.Should().NotBe("");
    }

    [Fact]
    public void Should_Set_ExternalDocs()
    {
        var crd = _mlc.Transpile(typeof(ExtDocsAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.ExternalDocs.Url.Should().NotBe("");
    }

    [Fact]
    public void Should_Set_ExternalDocs_Description()
    {
        var crd = _mlc.Transpile(typeof(ExtDocsWithDescriptionAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.ExternalDocs.Description.Should().NotBe("");
    }

    [Fact]
    public void Should_Set_Items_Information()
    {
        var crd = _mlc.Transpile(typeof(ItemsAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];

        specProperties.Type.Should().Be("array");
        (specProperties.Items as V1JSONSchemaProps)?.Type?.Should().Be("string");
        specProperties.MaxItems.Should().Be(42);
        specProperties.MinItems.Should().Be(13);
    }

    [Fact]
    public void Should_Set_Length_Information()
    {
        var crd = _mlc.Transpile(typeof(LengthAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];

        specProperties.MinLength.Should().Be(2);
        specProperties.MaxLength.Should().Be(42);
    }

    [Fact]
    public void Should_Set_MultipleOf()
    {
        var crd = _mlc.Transpile(typeof(MultipleOfAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];

        specProperties.MultipleOf.Should().Be(2);
    }

    [Fact]
    public void Should_Set_Pattern()
    {
        var crd = _mlc.Transpile(typeof(PatternAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];

        specProperties.Pattern.Should().Be(@"/\d*/");
    }

    [Fact]
    public void Should_Set_RangeMinimum()
    {
        var crd = _mlc.Transpile(typeof(RangeMinimumAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];

        specProperties.Minimum.Should().Be(15);
        specProperties.ExclusiveMinimum.Should().BeTrue();
    }

    [Fact]
    public void Should_Set_RangeMaximum()
    {
        var crd = _mlc.Transpile(typeof(RangeMaximumAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];

        specProperties.Maximum.Should().Be(15);
        specProperties.ExclusiveMaximum.Should().BeTrue();
    }

    [Fact]
    public void Should_Set_Required()
    {
        var crd = _mlc.Transpile(typeof(RequiredAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["spec"];
        specProperties.Required.Should().Contain("property");
    }

    [Fact]
    public void Should_Not_Contain_Ignored_Property()
    {
        var crd = _mlc.Transpile(typeof(IgnoreAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["spec"];
        specProperties.Properties.Should().NotContainKey("property");
    }

    [Fact]
    public void Should_Set_Preserve_Unknown_Fields()
    {
        var crd = _mlc.Transpile(typeof(PreserveUnknownFieldsAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.XKubernetesPreserveUnknownFields.Should().BeTrue();
    }

    [Fact]
    public void Should_Set_EmbeddedResource_Fields()
    {
        var crd = _mlc.Transpile(typeof(EmbeddedResourceAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.XKubernetesEmbeddedResource.Should().BeTrue();
    }

    [Fact]
    public void Should_Set_Preserve_Unknown_Fields_On_Dictionaries()
    {
        var crd = _mlc.Transpile(typeof(SimpleDictionaryEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.XKubernetesPreserveUnknownFields.Should().BeTrue();
    }

    [Fact]
    public void Should_Set_Preserve_Unknown_Fields_On_Classes()
    {
        var crd = _mlc.Transpile(typeof(UnknownFieldsEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["spec"];
        specProperties.XKubernetesPreserveUnknownFields.Should().BeTrue();
    }

    [Fact]
    public void Should_Set_Preserve_Unknown_Fields_On_System_Object()
    {
        var crd = _mlc.Transpile(typeof(EntityWithSystemObject));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["spec"].Properties["obj"];
        specProperties.XKubernetesPreserveUnknownFields.Should().BeTrue();
    }

    [Fact]
    public void Should_Set_Preserve_Unknown_Fields_On_ObjectLists()
    {
        var crd = _mlc.Transpile(typeof(UnknownFieldsListEntity));

        var specProperties = (V1JSONSchemaProps)crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["spec"].Properties["propertyList"].Items;
        specProperties.XKubernetesPreserveUnknownFields.Should().BeTrue();
    }

    [Fact]
    public void Should_Not_Set_Preserve_Unknown_Fields_On_Generic_Dictionaries()
    {
        var crd = _mlc.Transpile(typeof(DictionaryEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.XKubernetesPreserveUnknownFields.Should().BeNull();
    }

    [Fact]
    public void Should_Not_Set_Preserve_Unknown_Fields_On_KeyValuePair_Enumerable()
    {
        var crd = _mlc.Transpile(typeof(EnumerableKeyPairsEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.XKubernetesPreserveUnknownFields.Should().BeNull();
    }

    [Fact]
    public void Should_Not_Set_Properties_On_Dictionaries()
    {
        var crd = _mlc.Transpile(typeof(SimpleDictionaryEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.Properties.Should().BeNull();
    }

    [Fact]
    public void Should_Not_Set_Properties_On_Generic_Dictionaries()
    {
        var crd = _mlc.Transpile(typeof(DictionaryEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.Properties.Should().BeNull();
    }

    [Fact]
    public void Should_Not_Set_Properties_On_KeyValuePair_Enumerable()
    {
        var crd = _mlc.Transpile(typeof(EnumerableKeyPairsEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.Properties.Should().BeNull();
    }

    [Fact]
    public void Should_Set_AdditionalProperties_On_Dictionaries_For_Value_type()
    {
        var crd = _mlc.Transpile(typeof(DictionaryEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.AdditionalProperties.Should().NotBeNull();
    }

    [Fact]
    public void Should_Set_AdditionalProperties_On_KeyValuePair_For_Value_type()
    {
        var crd = _mlc.Transpile(typeof(EnumerableKeyPairsEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.AdditionalProperties.Should().NotBeNull();
    }

    [Fact]
    public void Should_Set_IntOrString()
    {
        var crd = _mlc.Transpile(typeof(IntstrOrStringEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.Properties.Should().BeNull();
        specProperties.XKubernetesIntOrString.Should().BeTrue();
    }

    [Fact]
    public void Should_Use_PropertyName_From_JsonPropertyAttribute()
    {
        var crd = _mlc.Transpile(typeof(JsonPropNameAttrEntity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties;
        specProperties.Should().Contain(p => p.Key == "otherName");
    }

    [Fact]
    public void Must_Not_Contain_Ignored_TopLevel_Properties()
    {
        var crd = _mlc.Transpile(typeof(Entity));

        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties;
        specProperties.Should().NotContainKeys("metadata", "apiVersion", "kind");
    }

    [Fact]
    public void Should_Add_AdditionalPrinterColumns()
    {
        var crd = _mlc.Transpile(typeof(AdditionalPrinterColumnAttrEntity));
        var apc = crd.Spec.Versions.First().AdditionalPrinterColumns;
        apc.Should().ContainSingle(def => def.JsonPath == ".property");
    }

    [Fact]
    public void Should_Add_AdditionalPrinterColumns_With_Prio()
    {
        var crd = _mlc.Transpile(typeof(AdditionalPrinterColumnWideAttrEntity));
        var apc = crd.Spec.Versions.First().AdditionalPrinterColumns;
        apc.Should().ContainSingle(def => def.JsonPath == ".property" && def.Priority == 1);
    }

    [Fact]
    public void Should_Add_AdditionalPrinterColumns_With_Name()
    {
        var crd = _mlc.Transpile(typeof(AdditionalPrinterColumnNameAttrEntity));
        var apc = crd.Spec.Versions.First().AdditionalPrinterColumns;
        apc.Should().ContainSingle(def => def.JsonPath == ".property" && def.Name == "OtherName");
    }

    [Fact]
    public void Should_Add_GenericAdditionalPrinterColumns()
    {
        var crd = _mlc.Transpile(typeof(GenericAdditionalPrinterColumnAttrEntity));
        var apc = crd.Spec.Versions.First().AdditionalPrinterColumns;

        apc.Should().NotBeNull();
        apc.Should().ContainSingle(def => def.JsonPath == ".metadata.namespace" && def.Name == "Namespace");
    }

    [Fact]
    public void Should_Correctly_Use_Entity_Scope_Attribute()
    {
        var scopedCrd = _mlc.Transpile(typeof(Entity));
        var clusterCrd = _mlc.Transpile(typeof(ScopeAttrEntity));

        scopedCrd.Spec.Scope.Should().Be("Namespaced");
        clusterCrd.Spec.Scope.Should().Be("Cluster");
    }

    [Fact]
    public void Should_Correctly_Get_Enum_Value_From_JsonStringEnumMemberNameAttribute()
    {
        var crd = _mlc.Transpile(typeof(NamedEnumEntity));
        var specProperties = crd.Spec.Versions.First().Schema.OpenAPIV3Schema.Properties["property"];
        specProperties.EnumProperty.Should().BeEquivalentTo(["enumValue1", "enumValue2"]);
    }

    #region Test Entity Classes

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class StringTestEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = string.Empty;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableStringTestEntity : CustomKubernetesEntity
    {
        public string? Property { get; set; } = string.Empty;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class IntTestEntity : CustomKubernetesEntity
    {
        public int Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableIntTestEntity : CustomKubernetesEntity
    {
        public int? Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class LongTestEntity : CustomKubernetesEntity
    {
        public long Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableLongTestEntity : CustomKubernetesEntity
    {
        public long? Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class FloatTestEntity : CustomKubernetesEntity
    {
        public float Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableFloatTestEntity : CustomKubernetesEntity
    {
        public float? Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class DecimalTestEntity : CustomKubernetesEntity
    {
        public decimal Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableDecimalTestEntity : CustomKubernetesEntity
    {
        public decimal? Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class DoubleTestEntity : CustomKubernetesEntity
    {
        public double Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableDoubleTestEntity : CustomKubernetesEntity
    {
        public double? Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class BoolTestEntity : CustomKubernetesEntity
    {
        public bool Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableBoolTestEntity : CustomKubernetesEntity
    {
        public bool? Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class DateTimeTestEntity : CustomKubernetesEntity
    {
        public DateTime Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableDateTimeTestEntity : CustomKubernetesEntity
    {
        public DateTime? Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class DateTimeOffsetTestEntity : CustomKubernetesEntity
    {
        public DateTimeOffset Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableDateTimeOffsetTestEntity : CustomKubernetesEntity
    {
        public DateTimeOffset? Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class V1ObjectMetaTestEntity : CustomKubernetesEntity
    {
        public V1ObjectMeta Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class ResourceQuantityTestEntity: CustomKubernetesEntity
    {
        public ResourceQuantity Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class StringArrayEntity : CustomKubernetesEntity
    {
        public string[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableStringArrayEntity : CustomKubernetesEntity
    {
        public string[]? Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EnumerableNullableIntEntity : CustomKubernetesEntity
    {
        public IEnumerable<int?> Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EnumerableIntEntity : CustomKubernetesEntity
    {
        public IEnumerable<int> Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class HashSetIntEntity : CustomKubernetesEntity
    {
        public HashSet<int> Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class SetIntEntity : CustomKubernetesEntity
    {
        public ISet<int> Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class InheritedEnumerableEntity : CustomKubernetesEntity
    {
        public IntegerList Property { get; set; } = null!;

        public class IntegerList : Collection<int>;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EnumEntity : CustomKubernetesEntity
    {
        public TestSpecEnum Property { get; set; }

        public enum TestSpecEnum
        {
            Value1,
            Value2,
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NullableEnumEntity : CustomKubernetesEntity
    {
        public TestSpecEnum? Property { get; set; }

        public enum TestSpecEnum
        {
            Value1,
            Value2,
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class NamedEnumEntity : CustomKubernetesEntity
    {
        public TestSpecEnum Property { get; set; }

        public enum TestSpecEnum
        {
            [JsonStringEnumMemberName("enumValue1")]
            Value1,
            [JsonStringEnumMemberName("enumValue2")]
            Value2,
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class SimpleDictionaryEntity : CustomKubernetesEntity
    {
        public IDictionary Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class UnknownFieldsEntity : CustomKubernetesEntity<UnknownFieldsEntity.EntitySpec>
    {
        [PreserveUnknownFields]
        public class EntitySpec;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EntityWithSystemObject : CustomKubernetesEntity<EntityWithSystemObject.EntitySpec>
    {
        public class EntitySpec
        {
            public object Obj { get; set; } = null!;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class UnknownFieldsListEntity : CustomKubernetesEntity<UnknownFieldsListEntity.EntitySpec>
    {
        public class EntitySpec
        {
            public List<ObjectList> PropertyList { get; set; } = null!;

            [PreserveUnknownFields]
            public class ObjectList;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class DictionaryEntity : CustomKubernetesEntity
    {
        public IDictionary<string, string> Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EnumerableKeyPairsEntity : CustomKubernetesEntity
    {
        public IEnumerable<KeyValuePair<string, string>> Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class IntstrOrStringEntity : CustomKubernetesEntity
    {
        public IntstrIntOrString Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EmbeddedResourceEntity : CustomKubernetesEntity
    {
        public V1Pod Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EmbeddedCustomResourceEntity : CustomKubernetesEntity
    {
        public EmbeddedCustomResource Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EmbeddedCustomResource : CustomKubernetesEntity
    {
        public string Property { get; set; } = string.Empty;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EmbeddedCustomResourceGenericEntity : CustomKubernetesEntity
    {
        public EmbeddedCustomResourceGeneric Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EmbeddedCustomResourceGeneric : CustomKubernetesEntity<EmbeddedCustomResourceGeneric.EntitySpec>
    {
        public class EntitySpec;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private class EmbeddedResourceListEntity : CustomKubernetesEntity
    {
        public IList<V1Pod> Property { get; set; } = null!;
    }

    [Ignore]
    [KubernetesEntity]
    private class IgnoredEntity : CustomKubernetesEntity;

    public class NonEntity;

    [KubernetesEntity(
        ApiVersion = "v1alpha1",
        Kind = "VersionedEntity",
        Group = "kubeops.test.dev",
        PluralName = "versionedentities")]
    public class V1Alpha1VersionedEntity : CustomKubernetesEntity;

    [KubernetesEntity(
        ApiVersion = "v1beta1",
        Kind = "VersionedEntity",
        Group = "kubeops.test.dev",
        PluralName = "versionedentities")]
    public class V1Beta1VersionedEntity : CustomKubernetesEntity;

    [KubernetesEntity(
        ApiVersion = "v2beta2",
        Kind = "VersionedEntity",
        Group = "kubeops.test.dev",
        PluralName = "versionedentities")]
    public class V2Beta2VersionedEntity : CustomKubernetesEntity;

    [KubernetesEntity(
        ApiVersion = "v2",
        Kind = "VersionedEntity",
        Group = "kubeops.test.dev",
        PluralName = "versionedentities")]
    public class V2VersionedEntity : CustomKubernetesEntity;

    [KubernetesEntity(
        ApiVersion = "v1",
        Kind = "VersionedEntity",
        Group = "kubeops.test.dev",
        PluralName = "versionedentities")]
    public class V1VersionedEntity : CustomKubernetesEntity;

    [KubernetesEntity(
        ApiVersion = "v1",
        Kind = "AttributeVersionedEntity",
        Group = "kubeops.test.dev",
        PluralName = "attributeversionedentities")]
    [StorageVersion]
    public class V1AttributeVersionedEntity : CustomKubernetesEntity;

    [KubernetesEntity(
        ApiVersion = "v2",
        Kind = "AttributeVersionedEntity",
        Group = "kubeops.test.dev",
        PluralName = "attributeversionedentities")]
    public class V2AttributeVersionedEntity : CustomKubernetesEntity;

    [KubernetesEntity(
        ApiVersion = "v1337",
        Kind = "Kind",
        Group = "Group",
        PluralName = "Plural")]
    public class Entity : CustomKubernetesEntity;

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class EntityWithStatus : CustomKubernetesEntity<EntityWithStatus.EntitySpec, EntityWithStatus.EntityStatus>
    {
        public class EntitySpec;

        public class EntityStatus;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [KubernetesEntityShortNames("foo", "bar", "baz")]
    public class ShortnamesEntity : CustomKubernetesEntity;

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class DescriptionAttrEntity : CustomKubernetesEntity
    {
        [Description("Description")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class ExtDocsAttrEntity : CustomKubernetesEntity
    {
        [ExternalDocs("url")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class ExtDocsWithDescriptionAttrEntity : CustomKubernetesEntity
    {
        [ExternalDocs("url", "description")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class ItemsAttrEntity : CustomKubernetesEntity
    {
        [Items(13, 42)]
        public string[] Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class LengthAttrEntity : CustomKubernetesEntity
    {
        [Length(2, 42)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class MultipleOfAttrEntity : CustomKubernetesEntity
    {
        [MultipleOf(2)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class PatternAttrEntity : CustomKubernetesEntity
    {
        [Pattern(@"/\d*/")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class RangeMinimumAttrEntity : CustomKubernetesEntity
    {
        [RangeMinimum(15, true)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class RangeMaximumAttrEntity : CustomKubernetesEntity
    {
        [RangeMaximum(15, true)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class RequiredAttrEntity : CustomKubernetesEntity<RequiredAttrEntity.EntitySpec>
    {
        public class EntitySpec
        {
            [Required]
            public string Property { get; set; } = null!;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class IgnoreAttrEntity : CustomKubernetesEntity<IgnoreAttrEntity.EntitySpec>
    {
        public class EntitySpec
        {
            [Ignore]
            public string Property { get; set; } = null!;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class PreserveUnknownFieldsAttrEntity : CustomKubernetesEntity
    {
        [PreserveUnknownFields]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class EmbeddedResourceAttrEntity : CustomKubernetesEntity
    {
        [EmbeddedResource]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class AdditionalPrinterColumnAttrEntity : CustomKubernetesEntity
    {
        [AdditionalPrinterColumn]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class AdditionalPrinterColumnWideAttrEntity : CustomKubernetesEntity
    {
        [AdditionalPrinterColumn(PrinterColumnPriority.WideView)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class AdditionalPrinterColumnNameAttrEntity : CustomKubernetesEntity
    {
        [AdditionalPrinterColumn(name: "OtherName")]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class ClassDescriptionAttrEntity : CustomKubernetesEntity<ClassDescriptionAttrEntity.EntitySpec>
    {
        [Description("Description")]
        public class EntitySpec;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [GenericAdditionalPrinterColumn(".metadata.namespace", "Namespace", "string")]
    public class GenericAdditionalPrinterColumnAttrEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [EntityScope(EntityScope.Cluster)]
    public class ScopeAttrEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public class JsonPropNameAttrEntity : CustomKubernetesEntity
    {
        [JsonPropertyName("otherName")]
        public string Property { get; set; } = null!;
    }

    #endregion
}
