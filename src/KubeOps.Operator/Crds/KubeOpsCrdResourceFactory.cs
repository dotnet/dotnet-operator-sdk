// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Runtime.InteropServices;

using k8s.Models;

using KubeOps.Abstractions.Crds;
using KubeOps.Transpiler;

namespace KubeOps.Operator.Crds;

/// <summary>
/// Default implementation of <see cref="ICrdResourceFactory"/> that uses the transpiler
/// to generate CRD definitions from entity types.
/// </summary>
public class KubeOpsCrdResourceFactory : ICrdResourceFactory
{
    /// <inheritdoc />
    public IEnumerable<V1CustomResourceDefinition> CreateCustomResourceDefinitions(IReadOnlyCollection<Type> entityTypes)
    {
        var assemblyDirectories = entityTypes
            .Select(t => Path.GetDirectoryName(t.Assembly.Location)!)
            .Distinct();

        using var mlc = ContextCreator.Create(
            Directory
                .GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
                .Concat(assemblyDirectories.SelectMany(dir => Directory.GetFiles(dir, "*.dll")))
                .Distinct(),
            coreAssemblyName: typeof(object).Assembly.GetName().Name);

        foreach (var entityType in entityTypes)
        {
            yield return CreateCrdForEntityType(mlc, entityType);
        }
    }

    /// <summary>
    /// Creates a <see cref="V1CustomResourceDefinition"/> using the provided <see cref="MetadataLoadContext"/>.
    /// Override this method to customize CRD generation while reusing the default context creation logic.
    /// </summary>
    /// <param name="context">The <see cref="MetadataLoadContext"/> used for type resolution.</param>
    /// <param name="entityType">The entity type to transpile into a CRD.</param>
    /// <returns>The generated <see cref="V1CustomResourceDefinition"/>.</returns>
    protected virtual V1CustomResourceDefinition CreateCrdForEntityType(MetadataLoadContext context, Type entityType)
        => context.Transpile(entityType);
}
