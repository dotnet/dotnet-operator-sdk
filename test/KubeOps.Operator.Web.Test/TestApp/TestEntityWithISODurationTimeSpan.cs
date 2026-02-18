// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Operator.Web.Test.TestApp;

[KubernetesEntity(Group = "test.kubeops.dev", ApiVersion = "v1", Kind = "TestEntity")]
public sealed class TestEntityWithISODurationTimeSpan : CustomKubernetesEntity<TestEntityWithISODurationTimeSpan.EntitySpec>
{
    public TestEntityWithISODurationTimeSpan()
    {
        ApiVersion = "test.kubeops.dev/v1";
        Kind = "TestEntity";
    }

    public sealed class EntitySpec
    {
        public string Value { get; set; } = string.Empty;

        public TimeSpan Timeout { get; set; }
    }
}
