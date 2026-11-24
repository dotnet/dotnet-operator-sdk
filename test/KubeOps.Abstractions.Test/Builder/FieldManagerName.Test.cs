// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;

namespace KubeOps.Abstractions.Test.Builder;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.Configuration)]
public sealed class FieldManagerNameTest
{
    public static TheoryData<string> ValidFieldManagers =>
    [
        "my-operator",
        "kubernetes-csharp",
        "My Operator v1.2 (beta)",
        "öperator-☸",
        new string('a', FieldManagerName.MaxLength),
    ];

    public static TheoryData<string> InvalidFieldManagers =>
    [
        string.Empty,
        new string('a', FieldManagerName.MaxLength + 1),

        // 65 characters, but 130 bytes in UTF-8: the API server counts bytes.
        new string('ü', 65),
        "my\noperator",
        "my\toperator",
        WithCodePoint(0x00A0), // no-break space
        WithCodePoint(0x200B), // zero width space (format)
        WithCodePoint(0x2028), // line separator
        WithCodePoint(0xE000), // private use
        WithCodePoint(0xD800), // lone surrogate
    ];

    [Theory]
    [MemberData(nameof(ValidFieldManagers))]
    public void ThrowIfInvalid_Accepts_Valid_Field_Managers(string fieldManager)
    {
        var act = () => FieldManagerName.ThrowIfInvalid(fieldManager);

        act.Should().NotThrow();
    }

    [Theory]
    // Not enumerated at discovery: serializing the lone surrogate would replace it with U+FFFD.
    [MemberData(nameof(InvalidFieldManagers), DisableDiscoveryEnumeration = true)]
    public void ThrowIfInvalid_Rejects_Invalid_Field_Managers(string fieldManager)
    {
        var act = () => FieldManagerName.ThrowIfInvalid(fieldManager);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(fieldManager));
    }

    [Fact]
    public void ThrowIfInvalid_Rejects_Null()
    {
        string fieldManager = null!;

        var act = () => FieldManagerName.ThrowIfInvalid(fieldManager);

        act.Should().Throw<ArgumentNullException>().WithParameterName(nameof(fieldManager));
    }

    private static string WithCodePoint(int codePoint) => $"my{(char)codePoint}operator";
}
