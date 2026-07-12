// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Operator.Web.Webhooks.Conversion;

namespace KubeOps.Operator.Web.Test.Webhooks.Conversion;

[RequiresPreviewFeatures]
public sealed class ConversionWebhookTest
{
    private const string Group = "kubeops.test";

    [Fact(DisplayName = "Converts between two non-storage versions by composing through the storage version")]
    public void Convert_ComposesThroughStorageVersion_ForTwoNonStorageVersions()
    {
        // v2 has no direct converter to v1; the only registered converters are v1<->v3 and v2<->v3 (v3 = storage).
        var result = Convert(Request(
            desired: $"{Group}/v1",
            @object: """{"apiVersion":"kubeops.test/v2","kind":"Subject","metadata":{"name":"s"},"spec":{"firstName":"Jane","lastName":"Doe"}}"""));

        var converted = result.Response.ConvertedObjects.Should().ContainSingle().Subject;
        converted.Should().BeOfType<V1Subject>().Which.Spec.FullName.Should().Be("Jane Doe");
    }

    [Fact(DisplayName = "Composes through the storage version in the reverse direction")]
    public void Convert_ComposesThroughStorageVersion_ReverseDirection()
    {
        var result = Convert(Request(
            desired: $"{Group}/v2",
            @object: """{"apiVersion":"kubeops.test/v1","kind":"Subject","metadata":{"name":"s"},"spec":{"fullName":"Jane Doe"}}"""));

        var converted = result.Response.ConvertedObjects.Should().ContainSingle().Subject;
        var spec = converted.Should().BeOfType<V2Subject>().Which.Spec;
        spec.FirstName.Should().Be("Jane");
        spec.LastName.Should().Be("Doe");
    }

    [Fact(DisplayName = "Direct conversion to the storage version still works")]
    public void Convert_DirectHopToStorageVersion_StillWorks()
    {
        var result = Convert(Request(
            desired: $"{Group}/v3",
            @object: """{"apiVersion":"kubeops.test/v1","kind":"Subject","metadata":{"name":"s"},"spec":{"fullName":"Jane Doe"}}"""));

        var converted = result.Response.ConvertedObjects.Should().ContainSingle().Subject;
        var spec = converted.Should().BeOfType<V3Subject>().Which.Spec;
        spec.FirstName.Should().Be("Jane");
        spec.LastName.Should().Be("Doe");
    }

    [Fact(DisplayName = "Direct conversion from the storage version still works")]
    public void Convert_DirectHopFromStorageVersion_StillWorks()
    {
        var result = Convert(Request(
            desired: $"{Group}/v1",
            @object: """{"apiVersion":"kubeops.test/v3","kind":"Subject","metadata":{"name":"s"},"spec":{"firstName":"Jane","lastName":"Doe"}}"""));

        var converted = result.Response.ConvertedObjects.Should().ContainSingle().Subject;
        converted.Should().BeOfType<V1Subject>().Which.Spec.FullName.Should().Be("Jane Doe");
    }

    [Fact(DisplayName = "Object with an unroutable source version is skipped")]
    public void Convert_SkipsObject_WithUnroutableSourceVersion()
    {
        var result = Convert(Request(
            desired: $"{Group}/v1",
            @object: """{"apiVersion":"kubeops.test/v9","kind":"Subject","metadata":{"name":"s"},"spec":{}}"""));

        result.Response.ConvertedObjects.Should().BeEmpty();
    }

    private static ConversionResponse Convert(ConversionRequest request) =>
        (ConversionResponse)new TestConversionWebhook().Convert(request);

    private static ConversionRequest Request(string desired, string @object) => new()
    {
        Request = new ConversionRequest.ConversionRequestData
        {
            Uid = "test-uid",
            DesiredApiVersion = desired,
            Objects = [JsonNode.Parse(@object)!],
        },
    };

    // Hub-and-spoke converter set exactly as the documentation prescribes: every served version converts to and from
    // the storage version (v3). There is deliberately no direct v1<->v2 converter.
    [RequiresPreviewFeatures]
    private sealed class TestConversionWebhook : ConversionWebhook<V3Subject>
    {
        protected override IEnumerable<IEntityConverter<V3Subject>> Converters => [new V1ToV3(), new V2ToV3()];
    }

    [RequiresPreviewFeatures]
    private sealed class V1ToV3 : IEntityConverter<V3Subject>
    {
        public Type FromType => typeof(V1Subject);

        public Type ToType => typeof(V3Subject);

        public string FromGroupVersion => $"{Group}/v1";

        public string ToGroupVersion => $"{Group}/v3";

        public V3Subject Convert(object from)
        {
            var source = (V1Subject)from;
            var parts = source.Spec.FullName.Split(' ', 2);
            return new V3Subject
            {
                Metadata = source.Metadata,
                Spec = { FirstName = parts[0], LastName = parts.Length > 1 ? parts[1] : string.Empty },
            };
        }

        public object Revert(V3Subject to) => new V1Subject
        {
            Metadata = to.Metadata,
            Spec = { FullName = $"{to.Spec.FirstName} {to.Spec.LastName}".Trim() },
        };
    }

    [RequiresPreviewFeatures]
    private sealed class V2ToV3 : IEntityConverter<V3Subject>
    {
        public Type FromType => typeof(V2Subject);

        public Type ToType => typeof(V3Subject);

        public string FromGroupVersion => $"{Group}/v2";

        public string ToGroupVersion => $"{Group}/v3";

        public V3Subject Convert(object from)
        {
            var source = (V2Subject)from;
            return new V3Subject { Metadata = source.Metadata, Spec = { FirstName = source.Spec.FirstName, LastName = source.Spec.LastName } };
        }

        public object Revert(V3Subject to) =>
            new V2Subject { Metadata = to.Metadata, Spec = { FirstName = to.Spec.FirstName, LastName = to.Spec.LastName } };
    }

    [KubernetesEntity(Group = Group, ApiVersion = "v1", Kind = "Subject")]
    private sealed class V1Subject : CustomKubernetesEntity<V1Subject.SpecDef>
    {
        public sealed class SpecDef
        {
            public string FullName { get; set; } = string.Empty;
        }
    }

    [KubernetesEntity(Group = Group, ApiVersion = "v2", Kind = "Subject")]
    private sealed class V2Subject : CustomKubernetesEntity<V2Subject.SpecDef>
    {
        public sealed class SpecDef
        {
            public string FirstName { get; set; } = string.Empty;

            public string LastName { get; set; } = string.Empty;
        }
    }

    [KubernetesEntity(Group = Group, ApiVersion = "v3", Kind = "Subject")]
    private sealed class V3Subject : CustomKubernetesEntity<V3Subject.SpecDef>
    {
        public sealed class SpecDef
        {
            public string FirstName { get; set; } = string.Empty;

            public string LastName { get; set; } = string.Empty;
        }
    }
}
