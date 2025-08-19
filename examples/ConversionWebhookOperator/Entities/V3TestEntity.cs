// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace ConversionWebhookOperator.Entities;

[KubernetesEntity(Group = "conversionwebhook.dev", ApiVersion = "v3", Kind = "TestEntity")]
public partial class V3TestEntity : CustomKubernetesEntity<V3TestEntity.EntitySpec>
{
    public override string ToString() => $"Test Entity v3 ({Metadata.Name}): {Spec.Firstname} {Spec.MiddleName} {Spec.Lastname}";

    public class EntitySpec
    {
        public string Firstname { get; set; } = string.Empty;

        public string Lastname { get; set; } = string.Empty;

        public string? MiddleName { get; set; }
    }
}
