// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Cli;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Error = 1;
    public const int Aborted = 2;
    public const int UsageError = 99;
}
