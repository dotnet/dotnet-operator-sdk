// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

using FluentAssertions;

using KubeOps.Generator.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KubeOps.Generator.Test;

public sealed partial class ControllerRegistrationGeneratorTest
{
    [Fact]
    public void Should_Report_Error_And_Skip_Registration_When_Controller_Declares_Both_Selectors()
    {
        const string input =
            """
            using k8s;
            using k8s.Models;
            using KubeOps.Abstractions.Entities;
            using KubeOps.Abstractions.Reconciliation.Controller;

            [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
            public sealed class V1TestEntity : IKubernetesObject<V1ObjectMeta>
            {
            }

            public sealed class MyLabelSelector : IEntityLabelSelector<V1TestEntity>
            {
                public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
                    ValueTask.FromResult<string?>("managed=true");
            }

            public sealed class MyFieldSelector : IEntityFieldSelector<V1TestEntity>
            {
                public ValueTask<string?> GetFieldSelectorAsync(CancellationToken cancellationToken) =>
                    ValueTask.FromResult<string?>("metadata.name=my-resource");
            }

            [LabelSelector(typeof(MyLabelSelector))]
            [FieldSelector(typeof(MyFieldSelector))]
            public sealed class V1TestEntityController : IEntityController<V1TestEntity>
            {
            }
            """;

        var inputCompilation = input.CreateCompilation();

        var driver = CSharpGeneratorDriver.Create(new ControllerRegistrationGenerator());
        driver.RunGeneratorsAndUpdateCompilation(
            inputCompilation,
            out var output,
            out ImmutableArray<Diagnostic> diagnostics,
            TestContext.Current.CancellationToken);

        diagnostics
            .Should().ContainSingle(d => d.Id == "KOG001")
            .Which.Severity.Should().Be(DiagnosticSeverity.Error);

        // The conflicting controller must not be registered (neither label- nor field-selector
        // variant); as it is the only controller, no registration class is generated at all.
        output.SyntaxTrees.Any(s => s.FilePath.Contains("ControllerRegistrations.g.cs")).Should().BeFalse();
    }

    [Fact]
    [Trait("Area", "Partial")]
    public void Should_Report_Error_When_Partial_Controller_Declares_Selectors_On_Separate_Parts()
    {
        const string input =
            """
            using k8s;
            using k8s.Models;
            using KubeOps.Abstractions.Entities;
            using KubeOps.Abstractions.Reconciliation.Controller;

            [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
            public sealed class V1TestEntity : IKubernetesObject<V1ObjectMeta>
            {
            }

            public sealed class MyLabelSelector : IEntityLabelSelector<V1TestEntity>
            {
                public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
                    ValueTask.FromResult<string?>("managed=true");
            }

            public sealed class MyFieldSelector : IEntityFieldSelector<V1TestEntity>
            {
                public ValueTask<string?> GetFieldSelectorAsync(CancellationToken cancellationToken) =>
                    ValueTask.FromResult<string?>("metadata.name=my-resource");
            }

            [LabelSelector(typeof(MyLabelSelector))]
            public partial class V1TestEntityController : IEntityController<V1TestEntity>
            {
            }

            [FieldSelector(typeof(MyFieldSelector))]
            public partial class V1TestEntityController : IEntityController<V1TestEntity>
            {
            }
            """;

        var inputCompilation = input.CreateCompilation();

        var driver = CSharpGeneratorDriver.Create(new ControllerRegistrationGenerator());
        driver.RunGeneratorsAndUpdateCompilation(
            inputCompilation,
            out var output,
            out ImmutableArray<Diagnostic> diagnostics,
            TestContext.Current.CancellationToken);

        diagnostics
            .Should().ContainSingle(d => d.Id == "KOG001")
            .Which.Severity.Should().Be(DiagnosticSeverity.Error);

        output.SyntaxTrees.Any(s => s.FilePath.Contains("ControllerRegistrations.g.cs")).Should().BeFalse();
    }
}
