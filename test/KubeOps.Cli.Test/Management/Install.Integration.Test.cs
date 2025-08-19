// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text;

using FluentAssertions;

using k8s;

using KubeOps.Cli.Commands.Generator;
using KubeOps.Cli.Commands.Management;

using Spectre.Console.Testing;

namespace KubeOps.Cli.Test.Management;

public class InstallIntegrationTest
{
    private static readonly string ProjectPath =
        Path.Join(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "examples", "Operator",
            "Operator.csproj");

    [Fact
        (Skip =
            "For some reason, the MetadataReferences are not loaded when the assembly parser is used from a test project.")
    ]
    public async Task Should_Install_Crds_In_Cluster()
    {
        var console = new TestConsole();
        var client = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
        var cmd = Install.Command;
        var result = cmd.Parse([ProjectPath, "-f"]);

        await Install.Handler(console, client, result);
    }

    [Fact
        (Skip = "We need to think of a good way to validate the kustomization given there is not real schema published")
    ]
    public async Task Should_Generate_Valid_Installers_In_Cluster()
    {
        var console = new TestConsole();
        var client = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
        var cmd = OperatorGenerator.Command;
        var result = cmd.Parse(["operator", "test", ProjectPath]);

        await OperatorGenerator.Handler(console, result);
        console.Output.Should().NotBeNull();
        var separator = console.Lines.GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key;
        var groups = new List<string>();
        var sb = new StringBuilder();
        foreach (string consoleLine in console.Lines)
        {
            if (consoleLine.Equals(separator))
            {
                groups.Add(sb.ToString());
                sb = new StringBuilder();
            }
            else
            {
                sb.AppendLine(consoleLine);
            }
        }

        foreach (string s in groups.Skip(1))
        {
            // Verify its valid kubernetes yaml
            var lines = s.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            // first line should be the filename:
            var filename = lines[0].Trim();
            var content = lines[1..].ToList();
            var file = filename.Split(" ")[1].Trim();
            var fileContent = string.Join(Environment.NewLine, content);
            // Assert there is a file name and valid kubernetesyaml
            file.Should().NotBeNullOrEmpty();
            fileContent.Should().NotBeNullOrEmpty();

            KubernetesYaml.Deserialize<dynamic>(fileContent)
                .Should().NotBeNull();

            // Assert the file named kustomization.yaml exists and is a valid Kustomization file
            if (file.Equals("kustomization.yaml"))
            {
            }
        }
    }
}
