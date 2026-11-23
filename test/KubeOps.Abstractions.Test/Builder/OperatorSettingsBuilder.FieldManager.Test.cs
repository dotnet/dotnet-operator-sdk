// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;

namespace KubeOps.Abstractions.Test.Builder;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Configuration)]
public sealed class OperatorSettingsBuilderFieldManagerTest
{
    [Fact]
    public void FieldManager_Defaults_To_The_Configured_Operator_Name()
    {
        var settings = new OperatorSettingsBuilder().WithName("my-operator").Build();

        settings.FieldManager.Should().Be("my-operator");
    }

    [Fact]
    public void FieldManager_Is_Taken_From_The_Name_At_Build_Time()
    {
        var builder = new OperatorSettingsBuilder();
        var before = builder.Build();

        builder.Name = "renamed-operator";

        before.FieldManager.Should().Be("kubernetesoperator");
        builder.Build().FieldManager.Should().Be("renamed-operator");
    }

    [Fact]
    public void FieldManager_Set_To_Null_Falls_Back_To_The_Operator_Name()
    {
        var builder = new OperatorSettingsBuilder().WithName("my-operator").WithFieldManager("my-field-manager");

        builder.FieldManager = null;

        builder.Build().FieldManager.Should().Be("my-operator");
    }

    [Fact]
    public void WithFieldManager_Rejects_An_Invalid_Field_Manager()
    {
        var builder = new OperatorSettingsBuilder();

        var act = () => builder.WithFieldManager("my\noperator");

        act.Should().Throw<ArgumentException>();
        builder.FieldManager.Should().BeNull();
    }

    [Fact]
    public void Build_Rejects_An_Operator_Name_That_Is_No_Valid_Field_Manager()
    {
        var builder = new OperatorSettingsBuilder().WithName(new string('a', FieldManagerName.MaxLength + 1));

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*WithFieldManager*");
    }

    [Fact]
    public void Build_Accepts_An_Operator_Name_That_Is_No_Valid_Field_Manager_With_An_Explicit_Field_Manager()
    {
        var settings = new OperatorSettingsBuilder()
            .WithName(new string('a', FieldManagerName.MaxLength + 1))
            .WithFieldManager("my-operator")
            .Build();

        settings.FieldManager.Should().Be("my-operator");
    }

    [Fact]
    public void Settings_Copied_With_Another_Name_Default_To_That_Name()
    {
        var settings = new OperatorSettingsBuilder().WithName("my-operator").Build() with { Name = "other-operator" };

        settings.FieldManager.Should().Be("other-operator");
    }

    [Fact]
    public void Settings_Copied_With_Another_Name_Keep_An_Explicit_Field_Manager()
    {
        var original = new OperatorSettingsBuilder().WithFieldManager("my-field-manager").Build();

        var settings = original with { Name = "other-operator" };

        settings.FieldManager.Should().Be("my-field-manager");
    }

    [Fact]
    public void Settings_Reject_An_Invalid_Field_Manager()
    {
        var settings = new OperatorSettingsBuilder().Build();

        var act = () => settings with { FieldManager = "my\noperator" };

        act.Should().Throw<ArgumentException>();
    }
}
