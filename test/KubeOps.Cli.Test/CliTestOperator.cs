// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Cli.Test;

/// <summary>
/// The operator project under test/KubeOps.Cli.Test.Operator that the integration tests run the CLI against.
/// </summary>
internal static class CliTestOperator
{
    public const string CrdName = "clitestentitys.clitest.kubeops.dev";

    public static readonly string ProjectPath = Path.GetFullPath(
        Path.Join(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "KubeOps.Cli.Test.Operator",
            "KubeOps.Cli.Test.Operator.csproj"));
}
