// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Generator.Discovery;

/// <summary>
/// Registration classes of a referenced assembly, discovered via the
/// <c>KubeOps.Abstractions.Builder.KubeOpsGeneratedRegistrationsAttribute</c> assembly marker.
/// The class names are fully qualified but carry no <c>global::</c> prefix.
/// </summary>
internal record struct ReferencedRegistrations(
    string AssemblyName,
    string? ControllerRegistrations,
    string? FinalizerRegistrations);
