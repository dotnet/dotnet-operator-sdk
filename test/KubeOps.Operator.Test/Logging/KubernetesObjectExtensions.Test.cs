// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Operator.Logging;

namespace KubeOps.Operator.Test.Logging;

public sealed class KubernetesObjectExtensionsTest
{
    [Fact]
    public void ToIdentifierString_Should_Include_Kind_Name_And_Uid_When_All_Present()
    {
        var obj = CreateObject(name: "my-config", uid: "abc-123");

        obj.ToIdentifierString().Should().Be($"{V1ConfigMap.KubeKind}/my-config (UID: abc-123)");
    }

    [Fact]
    public void ToIdentifierString_Should_Omit_Name_When_Name_Is_Absent()
    {
        var obj = CreateObject(name: null, uid: "abc-123");

        obj.ToIdentifierString().Should().Be($"{V1ConfigMap.KubeKind} (UID: abc-123)");
    }

    [Fact]
    public void ToIdentifierString_Should_Omit_Uid_When_Uid_Is_Absent()
    {
        var obj = CreateObject(name: "my-config", uid: null);

        obj.ToIdentifierString().Should().Be($"{V1ConfigMap.KubeKind}/my-config");
    }

    [Fact]
    public void ToIdentifierString_Should_Return_Only_Kind_When_Name_And_Uid_Are_Absent()
    {
        var obj = CreateObject(name: null, uid: null);

        obj.ToIdentifierString().Should().Be($"{V1ConfigMap.KubeKind}");
    }

    private static V1ConfigMap CreateObject(string? name, string? uid)
        => new()
        {
            Kind = V1ConfigMap.KubeKind,
            Metadata = new()
            {
                Name = name,
                Uid = uid,
            },
        };
}
