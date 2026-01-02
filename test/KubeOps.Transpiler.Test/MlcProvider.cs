// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Runtime.InteropServices;

using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;

namespace KubeOps.Transpiler.Test;

public class MlcProvider : IAsyncLifetime
{
    static MlcProvider()
    {
        MSBuildLocator.RegisterDefaults();
    }

    public MetadataLoadContext Mlc { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var assemblyConfigurationAttribute =
            typeof(MlcProvider).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
        var buildConfigurationName = assemblyConfigurationAttribute?.Configuration ?? "Debug";

        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            { "Configuration", buildConfigurationName },
        });

        workspace.SkipUnrecognizedProjects = true;
        workspace.LoadMetadataForReferencedProjects = true;
        var project = await workspace.OpenProjectAsync("../../../KubeOps.Transpiler.Test.csproj");

        Mlc = ContextCreator.Create(Directory
            .GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Concat(Directory.GetFiles(Path.GetDirectoryName(project.OutputFilePath)!, "*.dll"))
            .Distinct(), coreAssemblyName: typeof(object).Assembly.GetName().Name);
    }

    public ValueTask DisposeAsync()
    {
        Mlc.Dispose();
        return ValueTask.CompletedTask;
    }
}
