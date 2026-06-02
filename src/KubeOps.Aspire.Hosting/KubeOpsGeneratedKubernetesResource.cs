// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Aspire.Hosting.Kubernetes.Resources;

using YamlDotNet.Serialization;

namespace Aspire.Hosting;

internal sealed class KubeOpsGeneratedKubernetesResource(string apiVersion, string kind)
    : BaseKubernetesResource(apiVersion, kind)
{
    [YamlMember(Alias = "data")]
    public object? Data { get; set; }

    [YamlMember(Alias = "stringData")]
    public object? StringData { get; set; }

    [YamlMember(Alias = "type")]
    public object? Type { get; set; }

    [YamlMember(Alias = "spec")]
    public object? Spec { get; set; }

    [YamlMember(Alias = "rules")]
    public object? Rules { get; set; }

    [YamlMember(Alias = "roleRef")]
    public object? RoleRef { get; set; }

    [YamlMember(Alias = "subjects")]
    public object? Subjects { get; set; }

    [YamlMember(Alias = "webhooks")]
    public object? Webhooks { get; set; }

    [YamlMember(Alias = "secrets")]
    public object? Secrets { get; set; }

    [YamlMember(Alias = "imagePullSecrets")]
    public object? ImagePullSecrets { get; set; }

    public bool ShouldSerializeData() => Data is not null;

    public bool ShouldSerializeStringData() => StringData is not null;

    public bool ShouldSerializeType() => Type is not null;

    public bool ShouldSerializeSpec() => Spec is not null;

    public bool ShouldSerializeRules() => Rules is not null;

    public bool ShouldSerializeRoleRef() => RoleRef is not null;

    public bool ShouldSerializeSubjects() => Subjects is not null;

    public bool ShouldSerializeWebhooks() => Webhooks is not null;

    public bool ShouldSerializeSecrets() => Secrets is not null;

    public bool ShouldSerializeImagePullSecrets() => ImagePullSecrets is not null;
}
