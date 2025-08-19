// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines a property as an embedded resource.
/// This property can contain another Kubernetes object
/// (e.g. a <see cref="V1ConfigMap"/> or a <see cref="V1Deployment"/>).
/// This implicitly sets the <see cref="PreserveUnknownFieldsAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class EmbeddedResourceAttribute : Attribute;
