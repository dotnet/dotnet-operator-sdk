// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Cli.Output;

namespace KubeOps.Cli.Generators;

internal class DeploymentGenerator(OutputFormat format) : IConfigGenerator
{
    public void Generate(ResultOutput output)
    {
        var deployment = new V1Deployment(metadata: new V1ObjectMeta(
            labels: new Dictionary<string, string> { { "operator-deployment", "kubernetes-operator" } },
            name: "operator")).Initialize();
        deployment.Spec = new V1DeploymentSpec
        {
            Replicas = 1,
            RevisionHistoryLimit = 0,
            Selector = new V1LabelSelector(
                matchLabels: new Dictionary<string, string> { { "operator-deployment", "kubernetes-operator" } }),
            Template = new V1PodTemplateSpec
            {
                Metadata = new V1ObjectMeta(
                    labels: new Dictionary<string, string> { { "operator-deployment", "kubernetes-operator" } }),
                Spec = new V1PodSpec
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
                                        new V1EnvVarSource
                                        {
                                            FieldRef = new V1ObjectFieldSelector
                                            {
                                                FieldPath = "metadata.namespace",
                                            },
                                        },
                                },
                            },
                            Resources = new V1ResourceRequirements
                            {
                                Requests = new Dictionary<string, ResourceQuantity>
                                {
                                    { "cpu", new ResourceQuantity("100m") },
                                    { "memory", new ResourceQuantity("64Mi") },
                                },
                                Limits = new Dictionary<string, ResourceQuantity>
                                {
                                    { "cpu", new ResourceQuantity("100m") },
                                    { "memory", new ResourceQuantity("128Mi") },
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
