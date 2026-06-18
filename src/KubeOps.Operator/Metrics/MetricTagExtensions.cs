// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Operator.Metrics;

/// <summary>
/// Maps reconciliation enums to stable, lower-case metric tag values.
/// </summary>
internal static class MetricTagExtensions
{
    public static string ToMetricString(this WatchEventType type) => type switch
    {
        WatchEventType.Added => "added",
        WatchEventType.Modified => "modified",
        WatchEventType.Deleted => "deleted",
        WatchEventType.Bookmark => "bookmark",
        WatchEventType.Error => "error",
        _ => type.ToString().ToLowerInvariant(),
    };

    public static string ToMetricString(this ReconciliationTriggerSource source) => source switch
    {
        ReconciliationTriggerSource.ApiServer => "api_server",
        ReconciliationTriggerSource.Operator => "operator",
        _ => source.ToString().ToLowerInvariant(),
    };

    public static string ToMetricString(this ReconciliationType type) => type switch
    {
        ReconciliationType.Added => "added",
        ReconciliationType.Modified => "modified",
        ReconciliationType.Deleted => "deleted",
        _ => type.ToString().ToLowerInvariant(),
    };
}
