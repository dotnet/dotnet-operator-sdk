// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Cli.Output;

using Spectre.Console;

namespace KubeOps.Cli.Generators;

internal interface IConfigGenerator
{
    void Generate(ResultOutput output);
}
