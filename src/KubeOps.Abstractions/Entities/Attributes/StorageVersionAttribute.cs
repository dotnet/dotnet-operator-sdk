// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// This attribute marks an entity as the storage version of
/// an entity. Only one storage version must be set.
/// If none of the versions define this attribute, the "newest"
/// one is taken according to the kubernetes versioning rules.
/// GA > Beta > Alpha > non versions.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class StorageVersionAttribute : Attribute;
