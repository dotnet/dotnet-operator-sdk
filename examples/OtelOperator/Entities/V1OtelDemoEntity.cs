// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace OtelOperator.Entities;

[KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "OtelDemoEntity")]
public sealed partial class V1OtelDemoEntity : CustomKubernetesEntity<V1OtelDemoEntity.EntitySpec>
{
    public sealed class EntitySpec
    {
        public string Message { get; set; } = string.Empty;
    }
}
