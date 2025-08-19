// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Attribute that states that the given entity or property should be
/// ignored during CRD generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class IgnoreAttribute : Attribute;
