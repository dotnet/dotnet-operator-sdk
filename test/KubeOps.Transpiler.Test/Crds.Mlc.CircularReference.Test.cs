// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace KubeOps.Transpiler.Test;

public sealed partial class CrdsMlcTest
{
    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Short_Circuit_Circular_Type_With_PreserveUnknownFields()
    {
        var crd = _mlc.Transpile(typeof(CircularPreserveUnknownFieldsEntity));

        var property = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["property"];
        property.Type.Should().Be("object");
        property.XKubernetesPreserveUnknownFields.Should().BeTrue();
        property.Properties.Should().BeNull();
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Throw_Descriptive_Exception_On_Unannotated_Circular_Type()
    {
        var act = () => _mlc.Transpile(typeof(CircularEntity));

        // The exception is prefixed with the affected entity so the failure is locatable.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*circular*")
            .WithMessage($"*{nameof(CircularEntity)}*");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Not_Throw_For_Shared_NonCircular_Type_Used_By_Siblings()
    {
        // Cycle detection is per recursion path: a non-recursive type referenced by two sibling
        // properties is not a cycle and must transpile without throwing.
        var crd = _mlc.Transpile(typeof(SharedTypeEntity));

        var spec = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["spec"];
        spec.Properties.Should().ContainKeys("first", "second");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Transpile_Circular_Type_When_Back_Reference_Is_Ignored()
    {
        var crd = _mlc.Transpile(typeof(IgnoredBackReferenceEntity));

        var node = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["spec"].Properties["node"];
        node.Properties.Should().ContainKey("name");
        node.Properties.Should().NotContainKey("parent");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Throw_On_Circular_Type_Through_Collection()
    {
        var act = () => _mlc.Transpile(typeof(CircularThroughCollectionEntity));

        act.Should().Throw<InvalidOperationException>().WithMessage("*circular*");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Throw_On_Circular_Type_Through_Dictionary_Value()
    {
        var act = () => _mlc.Transpile(typeof(CircularThroughDictionaryEntity));

        act.Should().Throw<InvalidOperationException>().WithMessage("*circular*");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Emit_Printer_Columns_For_Shared_Type_Under_Multiple_Paths()
    {
        // The printer-column cycle guard is path-scoped: a non-circular type reused under two
        // sibling properties must still contribute a column for each path.
        var crd = _mlc.Transpile(typeof(SharedPrinterColumnEntity));

        var apc = crd.Spec.Versions[0].AdditionalPrinterColumns;
        apc.Should().Contain(c => c.JsonPath == ".primary.state");
        apc.Should().Contain(c => c.JsonPath == ".secondary.state");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Keep_Known_Properties_For_NonCircular_PreserveUnknownFields()
    {
        // PreserveUnknownFields on a fully transpilable type keeps the structural schema of known
        // fields and additionally allows unknown ones — it does not discard the known properties.
        var crd = _mlc.Transpile(typeof(PreserveUnknownFieldsKnownPropertiesEntity));

        var property = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["property"];
        property.Type.Should().Be("object");
        property.XKubernetesPreserveUnknownFields.Should().BeTrue();
        property.Properties.Should().ContainKey("knownField");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Fall_Back_To_Opaque_For_NonRepresentable_PreserveUnknownFields()
    {
        // A non-representable member (here JsonElement) would normally throw; PreserveUnknownFields
        // opts the subtree out, so it falls back to an opaque object instead of failing.
        var act = () => _mlc.Transpile(typeof(PreserveUnknownFieldsNonRepresentableEntity));
        act.Should().NotThrow();

        var property = _mlc.Transpile(typeof(PreserveUnknownFieldsNonRepresentableEntity))
            .Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["property"];
        property.Type.Should().Be("object");
        property.XKubernetesPreserveUnknownFields.Should().BeTrue();
        property.Properties.Should().BeNull();
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Keep_Known_Properties_For_NonCircular_ClassLevel_PreserveUnknownFields()
    {
        // Class-level [PreserveUnknownFields] keeps structural mapping of known fields plus the flag
        // (same as before) — the property-level fallback does not apply to class-level annotations.
        var crd = _mlc.Transpile(typeof(ClassLevelPreserveKnownPropertiesEntity));

        var spec = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["spec"];
        spec.XKubernetesPreserveUnknownFields.Should().BeTrue();
        spec.Properties.Should().ContainKey("knownField");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Fall_Back_To_Opaque_For_Circular_ClassLevel_PreserveUnknownFields()
    {
        // Class-level [PreserveUnknownFields] opts the whole type out, consistent with property-level:
        // a circular type degrades to an opaque object instead of failing.
        var crd = _mlc.Transpile(typeof(CircularClassLevelPreserveEntity));

        var spec = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["spec"];
        spec.Type.Should().Be("object");
        spec.XKubernetesPreserveUnknownFields.Should().BeTrue();
        spec.Properties.Should().BeNull();
    }

    #region Test Entity Classes

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class CircularPreserveUnknownFieldsEntity : CustomKubernetesEntity
    {
        [PreserveUnknownFields]
        public SelfReferencingType Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class CircularEntity : CustomKubernetesEntity<CircularEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public NodeA Node { get; set; } = null!;
        }

        public sealed class NodeA
        {
            public NodeB? Next { get; set; }
        }

        public sealed class NodeB
        {
            public NodeA? Back { get; set; }
        }
    }

    private sealed class SelfReferencingType
    {
        public SelfReferencingType? Next { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class SharedTypeEntity : CustomKubernetesEntity<SharedTypeEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public Shared First { get; set; } = null!;

            public Shared Second { get; set; } = null!;
        }

        public sealed class Shared
        {
            public string Value { get; set; } = string.Empty;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class IgnoredBackReferenceEntity : CustomKubernetesEntity<IgnoredBackReferenceEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public TreeNode Node { get; set; } = null!;
        }

        public sealed class TreeNode
        {
            public string Name { get; set; } = string.Empty;

            [Ignore]
            public TreeNode? Parent { get; set; }
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class CircularThroughCollectionEntity
        : CustomKubernetesEntity<CircularThroughCollectionEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public Branch Root { get; set; } = null!;
        }

        public sealed class Branch
        {
            public List<Leaf> Leaves { get; set; } = null!;
        }

        public sealed class Leaf
        {
            public Branch? Owner { get; set; }
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class CircularThroughDictionaryEntity
        : CustomKubernetesEntity<CircularThroughDictionaryEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public Catalog Root { get; set; } = null!;
        }

        public sealed class Catalog
        {
            public Dictionary<string, Catalog> Children { get; set; } = null!;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class SharedPrinterColumnEntity : CustomKubernetesEntity
    {
        public Holder Primary { get; set; } = null!;

        public Holder Secondary { get; set; } = null!;

        public sealed class Holder
        {
            [AdditionalPrinterColumn]
            public string State { get; set; } = string.Empty;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class PreserveUnknownFieldsKnownPropertiesEntity : CustomKubernetesEntity
    {
        [PreserveUnknownFields]
        public KnownSpec Property { get; set; } = null!;

        public sealed class KnownSpec
        {
            public string KnownField { get; set; } = string.Empty;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class PreserveUnknownFieldsNonRepresentableEntity : CustomKubernetesEntity
    {
        [PreserveUnknownFields]
        public JsonElement Property { get; set; }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class ClassLevelPreserveKnownPropertiesEntity
        : CustomKubernetesEntity<ClassLevelPreserveKnownPropertiesEntity.EntitySpec>
    {
        [PreserveUnknownFields]
        public sealed class EntitySpec
        {
            public string KnownField { get; set; } = string.Empty;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class CircularClassLevelPreserveEntity
        : CustomKubernetesEntity<CircularClassLevelPreserveEntity.EntitySpec>
    {
        [PreserveUnknownFields]
        public sealed class EntitySpec
        {
            public EntitySpec? Self { get; set; }
        }
    }

    #endregion
}
