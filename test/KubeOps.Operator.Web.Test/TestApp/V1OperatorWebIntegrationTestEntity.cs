// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Operator.Web.Test.TestApp;

[KubernetesEntity(Group = "weboperator.test", ApiVersion = "v1", Kind = "WebOperatorIntegrationTest")]
public class V1OperatorWebIntegrationTestEntity : CustomKubernetesEntity<V1OperatorWebIntegrationTestEntity.EntitySpec,
    V1OperatorWebIntegrationTestEntity.EntityStatus>
{
    public V1OperatorWebIntegrationTestEntity()
    {
        ApiVersion = "weboperator.test/v1";
        Kind = "WebOperatorIntegrationTest";
    }

    public V1OperatorWebIntegrationTestEntity(string name, string username) : this()
    {
        Metadata.Name = name;
        Metadata.NamespaceProperty = "default";
        Spec.Username = username;
    }

    public override string ToString() => $"Test Entity ({Metadata.Name}): {Spec.Username}";

    public class EntitySpec
    {
        public string Username { get; set; } = string.Empty;
    }

    public class EntityStatus
    {
        public string Status { get; set; } = string.Empty;
    }
}
