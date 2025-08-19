// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace KubeOps.Transpiler.Test;

public class EntitiesTest
{
    [Theory]
    [InlineData(typeof(NamespaceEntity), "Namespaced", "namespaceentity", "namespaceentities", "testing.dev/v1")]
    [InlineData(typeof(ClusterEntity), "Cluster", "clusterentity", "clusterentities", "testing.dev/v1")]
    public void Should_Correctly_Parse_Metadata(
        Type entityType,
        string expectedScope,
        string singular,
        string plural,
        string groupVersion)
    {
        var (meta, scope) = Entities.ToEntityMetadata(entityType);

        scope.Should().Be(expectedScope);
        meta.SingularName.Should().Be(singular);
        meta.PluralName.Should().Be(plural);
        meta.GroupWithVersion.Should().Be(groupVersion);
    }

    #region Test Entity Classes

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "NamespaceEntity",
        PluralName = "NamespaceEntities")]
    public class NamespaceEntity : CustomKubernetesEntity;

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "ClusterEntity",
        PluralName = "clusterentities")]
    [EntityScope(EntityScope.Cluster)]
    public class ClusterEntity : CustomKubernetesEntity;

    #endregion
}
