// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

internal sealed class KubeOpsPublishAnnotation(
    string projectPath,
    KubeOpsKubernetesManifestOptions options) : IResourceAnnotation
{
    public string ProjectPath { get; } = projectPath;

    public KubeOpsKubernetesManifestOptions Options { get; } = options;
}
