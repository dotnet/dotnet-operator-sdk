// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;
using KubeOps.Transpiler.Exceptions;

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
        act.Should().Throw<TranspilationFailedException>()
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

        act.Should().Throw<TranspilationFailedException>().WithMessage("*circular*");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Throw_On_Circular_Type_Through_Dictionary_Value()
    {
        var act = () => _mlc.Transpile(typeof(CircularThroughDictionaryEntity));

        act.Should().Throw<TranspilationFailedException>().WithMessage("*circular*");
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

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Throw_On_Type_That_Is_A_Collection_Of_Itself()
    {
        // The ancestor set only grows in MapObjectType, so a type that *is* a collection of itself never
        // passed through a guarded frame and recursed until the stack overflowed.
        var act = () => _mlc.Transpile(typeof(SelfCollectionEntity));

        act.Should().Throw<TranspilationFailedException>().WithMessage("*circular*");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Throw_On_Type_That_Is_A_Dictionary_Of_Itself()
    {
        var act = () => _mlc.Transpile(typeof(SelfDictionaryEntity));

        act.Should().Throw<TranspilationFailedException>().WithMessage("*circular*");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Throw_On_Mutually_Recursive_Collection_Types()
    {
        var act = () => _mlc.Transpile(typeof(MutualCollectionEntity));

        act.Should().Throw<TranspilationFailedException>().WithMessage("*circular*");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Throw_On_Recursively_Constructed_Generic()
    {
        // Every expansion of Wrapper<Wrapper<T>> yields a previously unseen Type, so the identity-based
        // ancestor check never matches. The depth limit terminates the walk instead.
        var act = () => _mlc.Transpile(typeof(RecursiveGenericEntity));

        act.Should().Throw<TranspilationFailedException>()
            .WithMessage("*maximum nesting depth*")
            .WithMessage($"*{nameof(RecursiveGenericEntity)}*");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Not_Walk_Ignored_Property_When_Mapping_Printer_Columns()
    {
        // Printer-column discovery must respect [Ignore]: a column below an ignored property would point
        // at a JSON path that does not exist in the schema.
        var crd = _mlc.Transpile(typeof(IgnoredPrinterColumnEntity));

        crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties.Should().NotContainKey("ignored");
        (crd.Spec.Versions[0].AdditionalPrinterColumns ?? []).Should().NotContain(c => c.JsonPath == ".ignored.state");
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Transpile_Entity_Whose_Recursive_Generic_Is_Ignored()
    {
        // The schema walk skips ignored properties; before printer-column discovery did the same, this
        // entity kept the work list growing forever instead of transpiling.
        var act = () => _mlc.Transpile(typeof(IgnoredRecursiveGenericEntity));

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Area", "CircularReferences")]
    public void Should_Not_Throw_For_Legitimately_Nested_Generics()
    {
        // Guards against a false positive of the depth limit: nested collections are finite and must map.
        var crd = _mlc.Transpile(typeof(NestedGenericEntity));

        var matrix = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["spec"].Properties["matrix"];
        matrix.Type.Should().Be("array");
        matrix.Items.As<V1JSONSchemaProps>().Type.Should().Be("array");
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

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class SelfCollectionEntity : CustomKubernetesEntity<SelfCollectionEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public Tree Root { get; set; } = null!;
        }

        public sealed class Tree : List<Tree>;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class SelfDictionaryEntity : CustomKubernetesEntity<SelfDictionaryEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public Config Root { get; set; } = null!;
        }

        public sealed class Config : Dictionary<string, Config>;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class MutualCollectionEntity : CustomKubernetesEntity<MutualCollectionEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public Left Root { get; set; } = null!;
        }

        public sealed class Left : List<Right>;

        public sealed class Right : List<Left>;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class RecursiveGenericEntity : CustomKubernetesEntity<RecursiveGenericEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public Wrapper<string> Wrapped { get; set; } = null!;
        }

        public sealed class Wrapper<T>
        {
            public Wrapper<Wrapper<T>>? Inner { get; set; }
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class IgnoredRecursiveGenericEntity
        : CustomKubernetesEntity<IgnoredRecursiveGenericEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public string Data { get; set; } = string.Empty;

            [Ignore]
            public Wrapper<string>? Wrapped { get; set; }
        }

        public sealed class Wrapper<T>
        {
            public Wrapper<Wrapper<T>>? Inner { get; set; }
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class IgnoredPrinterColumnEntity : CustomKubernetesEntity
    {
        [Ignore]
        public Holder Ignored { get; set; } = null!;

        public sealed class Holder
        {
            [AdditionalPrinterColumn]
            public string State { get; set; } = string.Empty;
        }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    private sealed class NestedGenericEntity : CustomKubernetesEntity<NestedGenericEntity.EntitySpec>
    {
        public sealed class EntitySpec
        {
            public List<List<string>> Matrix { get; set; } = null!;
        }
    }

    #endregion
}
