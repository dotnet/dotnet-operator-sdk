// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines that array items must be unique.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class UniqueItemsAttribute : Attribute;
