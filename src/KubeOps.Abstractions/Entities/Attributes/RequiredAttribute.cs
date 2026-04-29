// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines a property of a specification as required.
/// When applied to a class (e.g. an EntitySpec class), marks the corresponding top-level
/// property (e.g. "spec") as required in the generated CRD schema.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public class RequiredAttribute : Attribute;
