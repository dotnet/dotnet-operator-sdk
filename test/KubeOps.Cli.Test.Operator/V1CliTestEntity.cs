// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Cli.Test.Operator;

[KubernetesEntity(Group = "clitest.kubeops.dev", ApiVersion = "v1", Kind = "CliTestEntity")]
public sealed class V1CliTestEntity : CustomKubernetesEntity<V1CliTestEntity.EntitySpec>
{
    public sealed class EntitySpec
    {
        public string Username { get; set; } = string.Empty;
    }
}
