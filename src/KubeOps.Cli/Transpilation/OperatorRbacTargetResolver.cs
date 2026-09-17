// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Cli.Transpilation;

/// <summary>
/// Combines the requested RBAC scope with the statically discovered operator watch scope.
/// </summary>
internal static class OperatorRbacTargetResolver
{
    public static OperatorRbacTarget Resolve(
        RbacScope requestedScope,
        OperatorWatchScope discoveredScope,
        string? operatorNamespace,
        string operatorName)
    {
        var discoveredNamespace = discoveredScope.Kind == OperatorWatchScopeKind.Namespaced
            ? discoveredScope.Namespace
            : null;
        var namespaced = requestedScope switch
        {
            RbacScope.Cluster => false,
            RbacScope.Namespaced => true,
            _ => discoveredScope.Kind == OperatorWatchScopeKind.Namespaced,
        };

        if (!namespaced)
        {
            return new(
                OperatorWatchScope.ClusterWide,
                operatorNamespace ?? $"{operatorName}-system",
                operatorNamespace is null,
                requestedScope == RbacScope.Auto);
        }

        if (operatorNamespace is not null
            && discoveredNamespace is not null
            && operatorNamespace != discoveredNamespace)
        {
            throw new InvalidOperationException(
                $"The deployment namespace '{operatorNamespace}' differs from the statically configured " +
                $"operator watch namespace '{discoveredNamespace}'. Separate deployment and watch namespaces " +
                "are not supported by generated manifests.");
        }

        var effectiveNamespace = operatorNamespace ?? discoveredNamespace ?? $"{operatorName}-system";

        return new(
            OperatorWatchScope.Namespaced(effectiveNamespace),
            effectiveNamespace,
            operatorNamespace is null && discoveredNamespace is null,
            requestedScope == RbacScope.Auto);
    }
}
