// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Operator.Web.Test.TestApp;

[KubernetesEntity(Group = "test.kubeops.dev", ApiVersion = "v1", Kind = "TestEntity")]
public sealed class TestEntityWithTimeSpanConverter : CustomKubernetesEntity<TestEntityWithTimeSpanConverter.EntitySpec>
{
    public TestEntityWithTimeSpanConverter()
    {
        ApiVersion = "test.kubeops.dev/v1";
        Kind = "TestEntity";
    }

    public sealed class EntitySpec
    {
        public string Value { get; set; } = string.Empty;

        [JsonConverter(typeof(TimeSpanConverter))]
        public TimeSpan Timeout { get; set; }
    }
}

public sealed class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => TimeSpan.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
}
