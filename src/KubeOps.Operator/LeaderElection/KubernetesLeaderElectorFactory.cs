// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.LeaderElection;
using k8s.LeaderElection.ResourceLock;

using KubeOps.Abstractions.Builder;
using KubeOps.KubernetesClient;

namespace KubeOps.Operator.LeaderElection;

internal sealed class KubernetesLeaderElectorFactory(
    IKubernetes kubernetes,
    IKubernetesClient client,
    OperatorSettings settings)
    : ILeaderElectorFactory
{
    public LeaderElector CreateElector() => new(new LeaderElectionConfig(new LeaseLock(
        kubernetes,
        client.GetCurrentNamespace(),
        $"{settings.Name}-leader",
        Environment.MachineName))
    {
        LeaseDuration = settings.LeaderElectionLeaseDuration,
        RenewDeadline = settings.LeaderElectionRenewDeadline,
        RetryPeriod = settings.LeaderElectionRetryPeriod,
    });
}
