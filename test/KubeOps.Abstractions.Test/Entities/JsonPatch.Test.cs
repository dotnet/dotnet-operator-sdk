// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json.Nodes;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Abstractions.Test.Entities;

[Trait("Area", "JsonPatch")]
public sealed class JsonPatchTest
{
    [Theory]
    [InlineData("""{"a":1}""", """{"a":1}""", "[]")]
    [InlineData("""{"a":1}""", """{"a":2}""", """[{"op":"replace","path":"/a","value":2}]""")]
    [InlineData("""{"a":1}""", """{}""", """[{"op":"remove","path":"/a"}]""")]
    [InlineData("""{}""", """{"a":1}""", """[{"op":"add","path":"/a","value":1}]""")]
    [InlineData("""{"a":1}""", """{"a":null}""", """[{"op":"replace","path":"/a","value":null}]""")]
    [InlineData("""{"a":null}""", """{"a":null}""", "[]")]
    [InlineData("""{}""", """{"a":null}""", """[{"op":"add","path":"/a","value":null}]""")]
    [InlineData("""{"a":{"b":1}}""", """{"a":[1]}""", """[{"op":"replace","path":"/a","value":[1]}]""")]
    [InlineData("""{"a":1}""", """[1]""", """[{"op":"replace","path":"","value":[1]}]""")]
    [InlineData(
        """{"a":1,"b":2,"c":3}""",
        """{"d":4,"c":5,"a":1}""",
        """[{"op":"remove","path":"/b"},{"op":"replace","path":"/c","value":5},{"op":"add","path":"/d","value":4}]""")]
    public void CreatePatch_Should_Diff_Objects(string from, string to, string expected) =>
        Diff(from, to).Should().Be(expected);

    [Theory]
    [InlineData("""{"app.kubernetes.io/name":"a"}""", "/app.kubernetes.io~1name")]
    [InlineData("""{"a~b":"a"}""", "/a~0b")]
    [InlineData("""{"~1":"a"}""", "/~01")]
    [InlineData("""{"":"a"}""", "/")]
    public void CreatePatch_Should_Escape_Keys_In_Paths(string from, string expectedPath) =>
        JsonNode.Parse(from).CreatePatch(JsonNode.Parse("{}")).Operations.Should().ContainSingle()
            .Which.Path.Should().Be(expectedPath);

    [Theory]
    [InlineData("[]", "[]", "[]")]
    [InlineData("[]", "[1,2]", """[{"op":"replace","path":"","value":[1,2]}]""")]
    [InlineData("[1,2]", "[]", """[{"op":"replace","path":"","value":[]}]""")]
    [InlineData("[1]", "[2]", """[{"op":"replace","path":"/0","value":2}]""")]
    [InlineData(
        "[1]",
        "[2,3,4]",
        """[{"op":"replace","path":"/0","value":2},{"op":"add","path":"/1","value":3},{"op":"add","path":"/2","value":4}]""")]
    [InlineData(
        "[1,2,3,4]",
        "[5,2]",
        """[{"op":"remove","path":"/3"},{"op":"remove","path":"/2"},{"op":"replace","path":"/0","value":5}]""")]
    [InlineData("[1,2]", "[2,1]", """[{"op":"replace","path":"/0","value":2},{"op":"replace","path":"/1","value":1}]""")]
    [InlineData("""[{"a":[1]}]""", """[{"a":[2]}]""", """[{"op":"replace","path":"/0/a/0","value":2}]""")]
    public void CreatePatch_Should_Diff_Arrays(string from, string to, string expected) =>
        Diff(from, to).Should().Be(expected);

    [Theory]
    [InlineData("1", "1.0", true)]
    [InlineData("100", "1e2", true)]
    [InlineData("1", "2", false)]
    [InlineData("1", "\"1\"", false)]
    [InlineData("\"a\"", "\"a\"", true)]
    [InlineData("\"\\u0061\"", "\"a\"", true)]
    [InlineData("\"a\"", "\"A\"", false)]
    [InlineData("true", "true", true)]
    [InlineData("true", "false", false)]
    [InlineData("1e400", "1e400", true)]
    [InlineData("1e400", "1e401", false)]
    public void CreatePatch_Should_Compare_Values(string from, string to, bool equivalent) =>
        JsonNode.Parse(from).CreatePatch(JsonNode.Parse(to)).Operations.Should().HaveCount(equivalent ? 0 : 1);

    [Fact]
    public void CreatePatch_Should_Compare_Values_That_Are_Not_Parsed()
    {
        JsonValue.Create(1).CreatePatch(JsonNode.Parse("1.0")).Operations.Should().BeEmpty();
        JsonValue.Create("a").CreatePatch(JsonValue.Create("a")).Operations.Should().BeEmpty();
        JsonValue.Create(1).CreatePatch(JsonValue.Create("1")).Operations.Should().ContainSingle();
    }

    [Fact]
    public void CreatePatch_Should_Compare_Null_Nodes()
    {
        ((JsonNode?)null).CreatePatch(null).Operations.Should().BeEmpty();
        ((JsonNode?)null).CreatePatch(JsonNode.Parse("1")).ToJsonString()
            .Should().Be("""[{"op":"replace","path":"","value":1}]""");
    }

    [Fact]
    public void CreatePatch_Should_Clone_Values()
    {
        var to = JsonNode.Parse("""{"a":{"b":1}}""");

        var operation = JsonNode.Parse("{}").CreatePatch(to).Operations.Should().ContainSingle().Subject;

        operation.Value!.Parent.Should().BeNull();
        to!["a"]!["b"] = 2;
        operation.Value.ToJsonString().Should().Be("""{"b":1}""");
    }

    [Fact]
    public void ToJsonString_Should_Serialize_All_Operations()
    {
        var patch = new JsonPatch(
            PatchOperation.Add("/a", JsonNode.Parse("1")),
            PatchOperation.Remove("/b"),
            PatchOperation.Replace("/c", null),
            PatchOperation.Replace("/d", JsonNode.Parse("""{"e":[1.0]}""")));

        patch.ToJsonString().Should().Be(
            """
            [{"op":"add","path":"/a","value":1},{"op":"remove","path":"/b"},{"op":"replace","path":"/c","value":null},{"op":"replace","path":"/d","value":{"e":[1.0]}}]
            """);
    }

    [Fact]
    public void ToJsonString_Should_Reject_Unknown_Operation_Type()
    {
        var patch = new JsonPatch(new PatchOperation((OperationType)42, "/a"));

        patch.Invoking(p => p.ToJsonString()).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToBase64String_Should_Encode_The_Json_String()
    {
        var patch = new JsonPatch(PatchOperation.Remove("/a"));

        Encoding.UTF8.GetString(Convert.FromBase64String(patch.ToBase64String())).Should().Be(patch.ToJsonString());
    }

    [Fact]
    public void ToKubernetesPatch_Should_Create_A_Json_Patch()
    {
        var patch = new JsonPatch(PatchOperation.Remove("/a"));

        var result = patch.ToKubernetesPatch();

        result.Type.Should().Be(V1Patch.PatchType.JsonPatch);
        result.Content.Should().Be(patch.ToJsonString());
    }

    [Fact]
    public void CreateJsonPatch_Should_Apply_A_Custom_Filter()
    {
        var from = new V1ConfigMap { Metadata = new() { Name = "a", ResourceVersion = "1" } };
        var to = new V1ConfigMap { Metadata = new() { Name = "b", ResourceVersion = "2" } };

        var patch = from.CreateJsonPatch(to, operations => operations.Where(o => o.Path.EndsWith("Version")).ToList());

        patch.Operations.Should().ContainSingle().Which.Path.Should().Be("/metadata/resourceVersion");
    }

    private static string Diff(string from, string to) =>
        JsonNode.Parse(from).CreatePatch(JsonNode.Parse(to)).ToJsonString();
}
