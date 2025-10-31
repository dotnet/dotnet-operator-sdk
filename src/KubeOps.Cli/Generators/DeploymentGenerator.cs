// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Cli.Output;

namespace KubeOps.Cli.Generators;

internal sealed class DeploymentGenerator(OutputFormat format) : IConfigGenerator
{
    public void Generate(ResultOutput output)
    {
        var deployment = new V1Deployment
        {
            Metadata = new()
            {
                Name = "operator",
                Labels = new Dictionary<string, string> { { "operator-deployment", "kubernetes-operator" } },
            },
        }.Initialize();
        deployment.Spec = new()
        {
            Replicas = 1,
            RevisionHistoryLimit = 0,
            Selector = new()
            {
                MatchLabels = new Dictionary<string, string> { { "operator-deployment", "kubernetes-operator" } },
            },
            Template = new()
            {
                Metadata = new()
                {
                    Labels = new Dictionary<string, string> { { "operator-deployment", "kubernetes-operator" } },
                },
                Spec = new()
                {
                    TerminationGracePeriodSeconds = 10,
                    Containers = new List<V1Container>
                    {
                        new()
                        {
                            Image = "operator",
                            Name = "operator",
                            Env = new List<V1EnvVar>
                            {
                                new()
                                {
                                    Name = "POD_NAMESPACE",
                                    ValueFrom =
                                        new()
                                        {
                                            FieldRef = new()
                                            {
                                                FieldPath = "metadata.namespace",
                                            },
                                        },
                                },
                            },
                            Resources = new()
                            {
                                Requests = new Dictionary<string, ResourceQuantity>
                                {
                                    { "cpu", new("100m") },
                                    { "memory", new("64Mi") },
                                },
                                Limits = new Dictionary<string, ResourceQuantity>
                                {
                                    { "cpu", new("100m") },
                                    { "memory", new("128Mi") },
                                },
                            },
                        },
                    },
                },
            },
        };
        output.Add($"deployment.{format.GetFileExtension()}", deployment);
    }
}
