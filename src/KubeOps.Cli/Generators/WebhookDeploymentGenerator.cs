// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Cli.Output;

namespace KubeOps.Cli.Generators;

internal sealed class WebhookDeploymentGenerator(OutputFormat format) : IConfigGenerator
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
                                        new()
                                        {
                                            FieldRef = new()
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
                            Ports = new List<V1ContainerPort> { new() { HostPort = 5001, Name = "https" } },
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

        output.Add(
            $"service.{format.GetFileExtension()}",
            new V1Service
            {
                Metadata = new() { Name = "operator" },
                Spec = new()
                {
                    Ports =
                        new List<V1ServicePort>
                        {
                            new()
                            {
                                Name = "https",
                                TargetPort = "https",
                                Port = 443,
                            },
                        },
                    Selector = new Dictionary<string, string>
                    {
                        { "operator-deployment", "kubernetes-operator" },
                    },
                },
            }.Initialize());
    }
}
