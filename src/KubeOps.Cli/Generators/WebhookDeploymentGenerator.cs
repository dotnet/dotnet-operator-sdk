// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s;
using k8s.Models;

using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;
using KubeOps.Transpiler;

namespace KubeOps.Cli.Generators;

internal class WebhookDeploymentGenerator(OutputFormat format) : IConfigGenerator
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
                matchLabels:
                new Dictionary<string, string> { { "operator-deployment", "kubernetes-operator" } }),
            Template = new V1PodTemplateSpec
            {
                Metadata = new V1ObjectMeta(
                    labels:
                    new Dictionary<string, string> { { "operator-deployment", "kubernetes-operator" }, }),
                Spec = new V1PodSpec
                {
                    TerminationGracePeriodSeconds = 10,
                    Volumes = new List<V1Volume>
                    {
                        new() { Name = "certificates", Secret = new() { SecretName = "webhook-cert" }, },
                        new() { Name = "ca-certificates", Secret = new() { SecretName = "webhook-ca" }, },
                    },
                    Containers = new List<V1Container>
                    {
                        new()
                        {
                            Image = "operator",
                            Name = "operator",
                            VolumeMounts = new List<V1VolumeMount>
                            {
                                new() { Name = "certificates", MountPath = "/certs", ReadOnlyProperty = true, },
                                new() { Name = "ca-certificates", MountPath = "/ca", ReadOnlyProperty = true, },
                            },
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
                            EnvFrom =
                                new List<V1EnvFromSource>
                                {
                                    new() { ConfigMapRef = new() { Name = "webhook-config" } },
                                },
                            Ports = new List<V1ContainerPort> { new(5001, name: "https"), },
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

        output.Add(
            $"service.{format.GetFileExtension()}",
            new V1Service(
                metadata: new V1ObjectMeta(name: "operator"),
                spec: new V1ServiceSpec
                {
                    Ports =
                        new List<V1ServicePort> { new() { Name = "https", TargetPort = "https", Port = 443, }, },
                    Selector = new Dictionary<string, string> { { "operator-deployment", "kubernetes-operator" }, },
                }).Initialize());
    }
}
