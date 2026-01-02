// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

namespace KubeOps.Generator.Test.Entities;

[KubernetesEntity(Group = EntityConstants.Group, ApiVersion = EntityConstants.ApiVersion, Kind = EntityConstants.Kind)]
public sealed class V1TestEntity : IKubernetesObject
{
    public V1TestEntity(string someProperty)
    {
        SomeProperty = someProperty;
    }

    public string SomeProperty { get; }

    public string ApiVersion { get; set; } = EntityConstants.ApiVersion;

    public string Kind { get; set; } = EntityConstants.ApiVersion;
}
