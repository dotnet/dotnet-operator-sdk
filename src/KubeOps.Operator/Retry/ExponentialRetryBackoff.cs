// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Operator.Retry;

internal static class ExponentialRetryBackoff
{
    public static TimeSpan GetDelayWithJitter(uint retryCount) => TimeSpan
        .FromSeconds(Math.Pow(2, Math.Clamp(retryCount, 0, 5)))
        .Add(TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)));
}
