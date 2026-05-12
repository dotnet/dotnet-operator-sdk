// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Operator.Crds;
using KubeOps.Operator.Test.TestEntities;

namespace KubeOps.Operator.Test.Crds;

public sealed class KubeOpsCrdResourceFactoryTest
{
    [Fact]
    public void Should_Create_Crd_For_Entity_Type()
    {
        var factory = new KubeOpsCrdResourceFactory();
        var crd = factory.CreateCustomResourceDefinitions([typeof(V1OperatorIntegrationTestEntity)]).First();

        crd.Should().NotBeNull();
        crd.Spec.Names.Kind.Should().Be("OperatorIntegrationTest");
        crd.Spec.Group.Should().Be("operator.test");
    }
}
