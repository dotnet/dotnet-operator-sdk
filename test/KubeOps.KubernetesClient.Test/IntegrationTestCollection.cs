// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.KubernetesClient.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public class IntegrationTestCollection
{
    public const string Name = "Integration Tests";
}

[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase;
