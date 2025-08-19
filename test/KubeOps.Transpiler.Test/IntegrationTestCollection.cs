// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace KubeOps.Transpiler.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public class TranspilerTestCollection : ICollectionFixture<MlcProvider>
{
    public const string Name = "Transpiler Tests";
}

[Collection(TranspilerTestCollection.Name)]
public abstract class TranspilerTestBase(MlcProvider provider)
{
    protected readonly MetadataLoadContext _mlc = provider.Mlc;
}
