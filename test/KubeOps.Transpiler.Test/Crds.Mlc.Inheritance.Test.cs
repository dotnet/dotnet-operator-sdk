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
    [Trait("Area", "Inheritance")]
    [Fact]
    public void Should_Inherit_GenericPrinterColumn_From_Base_Entity_Class()
    {
        var crd = _mlc.Transpile(typeof(DerivedFromBasePrinterColumnEntity));
        var apc = crd.Spec.Versions[0].AdditionalPrinterColumns;

        apc.Should().NotBeNull();
        apc.Should().ContainSingle(col =>
            col.JsonPath == ".status.foo" && col.Name == "Foo" && col.Type == "string");
    }

    [Trait("Area", "Inheritance")]
    [Fact]
    public void Should_Accumulate_PrinterColumns_From_All_Hierarchy_Levels()
    {
        var crd = _mlc.Transpile(typeof(DoublyDerivedPrinterColumnEntity));
        var apc = crd.Spec.Versions[0].AdditionalPrinterColumns;

        apc.Should().NotBeNull();
        apc.Should().Contain(col => col.Name == "Foo");
        apc.Should().Contain(col => col.Name == "Bar");
    }

    [Trait("Area", "Inheritance")]
    [Fact]
    public void Should_Recognize_Inherited_PrinterColumn_Attribute_Type()
    {
        var crd = _mlc.Transpile(typeof(EntityWithInheritedPrinterColumnAttrType));
        var apc = crd.Spec.Versions[0].AdditionalPrinterColumns;

        apc.Should().NotBeNull();
        apc.Should().ContainSingle(col =>
            col.JsonPath == ".status.conditions[?(@.type==\"Ready\")].status" &&
            col.Name == "Ready" &&
            col.Type == "string");
    }

    [Trait("Area", "Inheritance")]
    [Fact]
    public void Should_Support_Multiple_Inherited_PrinterColumn_Attribute_Types()
    {
        var crd = _mlc.Transpile(typeof(EntityWithMultipleInheritedPrinterColumnAttrTypes));
        var apc = crd.Spec.Versions[0].AdditionalPrinterColumns;

        apc.Should().NotBeNull();
        apc.Should().Contain(col => col.Name == "Ready");
        apc.Should().Contain(col => col.Name == "Reason");
    }

    #region Test Entity Classes

    [GenericAdditionalPrinterColumn(".status.foo", "Foo", "string")]
    private class BasePrinterColumnEntity : CustomKubernetesEntity;

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "DerivedPrinterColumn")]
    private sealed class DerivedFromBasePrinterColumnEntity : BasePrinterColumnEntity;

    [GenericAdditionalPrinterColumn(".status.bar", "Bar", "string")]
    private class MidPrinterColumnEntity : BasePrinterColumnEntity;

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "DoublyDerivedPrinterColumn")]
    private sealed class DoublyDerivedPrinterColumnEntity : MidPrinterColumnEntity;

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    private sealed class ReadyPrinterColumnAttribute : GenericAdditionalPrinterColumnAttribute
    {
        public ReadyPrinterColumnAttribute()
            : base(".status.conditions[?(@.type==\"Ready\")].status", "Ready", "string") { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    private sealed class ReasonPrinterColumnAttribute : GenericAdditionalPrinterColumnAttribute
    {
        public ReasonPrinterColumnAttribute()
            : base(".status.conditions[?(@.type==\"Ready\")].reason", "Reason", "string") { }
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "InheritedAttrType")]
    [ReadyPrinterColumn]
    private sealed class EntityWithInheritedPrinterColumnAttrType : CustomKubernetesEntity;

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "MultiInheritedAttrType")]
    [ReadyPrinterColumn]
    [ReasonPrinterColumn]
    private sealed class EntityWithMultipleInheritedPrinterColumnAttrTypes : CustomKubernetesEntity;

    #endregion
}
