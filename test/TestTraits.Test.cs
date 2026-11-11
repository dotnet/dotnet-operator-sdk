// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using FluentAssertions;

using Xunit.v3;

namespace KubeOps.TestTraits;

[Trait(TestTrait.Category, TestCategory.UnitTest)]
[Trait(TestTrait.Area, TestArea.TestSuite)]
public sealed class TestTraitsTest
{
    private static readonly HashSet<string> Areas = ConstantsOf(typeof(TestArea));
    private static readonly HashSet<string> Features = ConstantsOf(typeof(TestFeature));

    [Fact]
    public void Every_Test_Has_One_Category_And_One_Area_From_The_Defined_Values()
    {
        var violations = typeof(TestTraitsTest).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => method.IsDefined(typeof(FactAttribute), inherit: true))
                .SelectMany(method => Violations(type, method)))
            .ToList();

        violations.Should().BeEmpty();
    }

    private static IEnumerable<string> Violations(Type type, MethodInfo method)
    {
        var traits = method.GetCustomAttributes(inherit: true)
            .Concat(type.GetCustomAttributes(inherit: true))
            .OfType<ITraitAttribute>()
            .SelectMany(attribute => attribute.GetTraits())
            .Distinct()
            .ToLookup(trait => trait.Key, trait => trait.Value);
        var test = $"{type.FullName}.{method.Name}";
        var expectedCategory = type.Name.EndsWith(TestCategory.IntegrationTest, StringComparison.Ordinal)
            ? TestCategory.IntegrationTest
            : TestCategory.UnitTest;

        if (!traits[TestTrait.Category].SequenceEqual([expectedCategory]))
        {
            yield return $"{test}: Category must be {expectedCategory}, " +
                         $"found [{string.Join(", ", traits[TestTrait.Category])}]";
        }

        var areas = traits[TestTrait.Area].ToList();
        if (areas.Count != 1 || !Areas.Contains(areas[0]))
        {
            yield return $"{test}: needs exactly one Area from TestArea, found [{string.Join(", ", areas)}]";
        }

        foreach (var feature in traits[TestTrait.Feature].Where(feature => !Features.Contains(feature)))
        {
            yield return $"{test}: Feature {feature} is not defined in TestFeature";
        }
    }

    private static HashSet<string> ConstantsOf(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet();
}
