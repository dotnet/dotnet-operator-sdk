// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

using FluentAssertions;

using KubeOps.Generator.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KubeOps.Generator.Test;

public class OperatorBuilderGeneratorTest
{
    [Fact]
    public void Should_Generate_Correct_Code()
    {
        var inputCompilation = string.Empty.CreateCompilation();
        var expectedResult =
            """
                // <auto-generated>
                // This code was generated by a tool.
                // Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                // </auto-generated>
                #pragma warning disable CS1591
                using KubeOps.Abstractions.Builder;

                public static class OperatorBuilderExtensions
                {
                    public static IOperatorBuilder RegisterComponents(this IOperatorBuilder builder)
                    {
                        builder.RegisterControllers();
                        builder.RegisterFinalizers();
                        return builder;
                    }
                }
                """.ReplaceLineEndings();

        var driver = CSharpGeneratorDriver.Create(new OperatorBuilderGenerator());
        driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out var output, out ImmutableArray<Diagnostic> _);

        var result = output.SyntaxTrees
            .First(s => s.FilePath.Contains("OperatorBuilder.g.cs"))
            .ToString().ReplaceLineEndings();
        result.Should().Be(expectedResult);
    }
}
