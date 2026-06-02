// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

internal sealed class KubeOpsRunAnnotation(KubeOpsRunOptions options) : IResourceAnnotation
{
    public KubeOpsRunOptions Options { get; } = options;

    public ISet<string> CreatedCrds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
