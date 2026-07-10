// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KubeOps.Generator.Test;

internal static class TestHelperExtensions
{
    public static Compilation CreateCompilation(this string source, params MetadataReference[] additionalReferences)
        => source.CreateCompilation("compilation", additionalReferences);

    public static Compilation CreateCompilation(
        this string source,
        string assemblyName,
        params MetadataReference[] additionalReferences)
        => CSharpCompilation.Create(
            assemblyName,
            [
                CSharpSyntaxTree.ParseText(source),
            ],
            [
                MetadataReference.CreateFromFile(typeof(Binder).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(typeof(Abstractions.Reconciliation.Controller.IEntityController<>).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(k8s.IKubernetesObject<>).GetTypeInfo().Assembly.Location),
                ..additionalReferences,
            ],
            new(OutputKind.DynamicallyLinkedLibrary));

    public static MetadataReference EmitToImageReference(this Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Emit of test compilation failed: {string.Join(Environment.NewLine, result.Diagnostics)}");
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    public static AnalyzerConfigOptionsProvider CreateGeneratorNamespaceOptions(string generatorNamespace)
        => new TestAnalyzerConfigOptionsProvider(
            new KeyValuePair<string, string>("build_property.KubeOpsGeneratorNamespace", generatorNamespace));

    private sealed class TestAnalyzerConfigOptionsProvider(params KeyValuePair<string, string>[] options)
        : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions(options);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new TestAnalyzerConfigOptions([]);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new TestAnalyzerConfigOptions([]);
    }

    private sealed class TestAnalyzerConfigOptions(KeyValuePair<string, string>[] options)
        : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _options = options.ToDictionary(o => o.Key, o => o.Value);

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            => _options.TryGetValue(key, out value);
    }
}
