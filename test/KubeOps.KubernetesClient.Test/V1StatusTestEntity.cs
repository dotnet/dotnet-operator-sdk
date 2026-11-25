// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.KubernetesClient.Test;

[KubernetesEntity(Group = "client.test", ApiVersion = "v1", Kind = "StatusTest", PluralName = "statustests")]
public sealed class V1StatusTestEntity
    : CustomKubernetesEntity<V1StatusTestEntity.EntitySpec, V1StatusTestEntity.EntityStatus>
{
    public V1StatusTestEntity()
    {
        ApiVersion = "client.test/v1";
        Kind = "StatusTest";
    }

    public sealed class EntitySpec
    {
        public int Replicas { get; set; }
    }

    public sealed class EntityStatus
    {
        public string? Phase { get; set; }

        public string? Message { get; set; }

        public List<Condition> Conditions { get; set; } = [];

        public Address Endpoint { get; set; } = new();
    }

    public sealed class Condition
    {
        public string Type { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
    }

    public sealed class Address
    {
        public string Host { get; set; } = string.Empty;

        public int Port { get; set; }
    }
}
