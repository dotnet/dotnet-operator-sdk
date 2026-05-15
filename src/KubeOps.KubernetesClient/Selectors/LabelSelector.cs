// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.Selectors;

/// <summary>
/// Different label selectors for querying the Kubernetes API.
/// </summary>
/// <seealso href="https://kubernetes.io/docs/concepts/overview/working-with-objects/labels/#label-selectors">Kubernetes Label Selectors</seealso>
#pragma warning disable S2094
public abstract record LabelSelector : KubernetesSelector;
#pragma warning restore S2094
