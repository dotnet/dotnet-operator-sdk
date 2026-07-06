// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.LeaderElection;

namespace KubeOps.Operator.LeaderElection;

/// <summary>
/// Shared leadership-callback wiring for the operator's leader-aware hosted services (the queue consumer and the
/// resource watcher). It owns the subscription of a service's <c>OnStartedLeading</c> / <c>OnStoppedLeading</c>
/// handlers to a <see cref="LeaderElector"/> and guarantees they are removed again.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Subscribe"/> and <see cref="Unsubscribe"/> are idempotent, so repeated calls cannot duplicate or
/// over-remove handlers.
/// </para>
/// <para>
/// It deliberately does <strong>not</strong> dispose the <see cref="LeaderElector"/>: the elector is a DI singleton
/// shared with the <c>LeaderElectionBackgroundService</c>, so its lifetime is not owned here.
/// </para>
/// </remarks>
/// <param name="elector">The shared leader elector to subscribe to.</param>
/// <param name="onStartedLeading">The handler to invoke when leadership is acquired.</param>
/// <param name="onStoppedLeading">The handler to invoke when leadership is lost.</param>
internal sealed class LeaderElectionSubscription(
    LeaderElector elector, Action onStartedLeading, Action onStoppedLeading)
{
    private readonly object _gate = new();
    private bool _subscribed;

    /// <summary>Gets a value indicating whether this instance currently holds leadership.</summary>
    public bool IsLeader => elector.IsLeader();

    /// <summary>Subscribes the handlers to the elector. A no-op if already subscribed.</summary>
    public void Subscribe()
    {
        lock (_gate)
        {
            if (_subscribed)
            {
                return;
            }

            elector.OnStartedLeading += onStartedLeading;
            elector.OnStoppedLeading += onStoppedLeading;
            _subscribed = true;
        }
    }

    /// <summary>Removes the handlers from the elector. A no-op if not subscribed.</summary>
    public void Unsubscribe()
    {
        lock (_gate)
        {
            if (!_subscribed)
            {
                return;
            }

            elector.OnStartedLeading -= onStartedLeading;
            elector.OnStoppedLeading -= onStoppedLeading;
            _subscribed = false;
        }
    }
}
