// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Entities;

namespace KubeOps.Generator.Test.Entities;

[KubernetesEntity(Group = EntityConstants.Group, ApiVersion = EntityConstants.ApiVersion, Kind = EntityConstants.Kind, PluralName = EntityConstants.Plural)]
public sealed class V1TestEntityFromReferencedAssemblyWithConstantValues : CustomKubernetesEntity;
