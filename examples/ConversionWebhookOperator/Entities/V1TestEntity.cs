// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace ConversionWebhookOperator.Entities;

[KubernetesEntity(Group = "conversionwebhook.dev", ApiVersion = "v1", Kind = "TestEntity")]
public partial class V1TestEntity : CustomKubernetesEntity<V1TestEntity.EntitySpec>
{
    public override string ToString() => $"Test Entity v1 ({Metadata.Name}): {Spec.Name}";

    public class EntitySpec
    {
        public string Name { get; set; } = string.Empty;
    }
}
