// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Operator.Queue;

/// <summary>
/// Optional capability of an <see cref="ITimedEntityQueue{TEntity}"/> that lets the operator gate and
/// drain the queue across a leadership transition.
/// </summary>
/// <remarks>
/// <para>
/// With <see cref="Abstractions.Builder.LeaderElectionType.Single"/> the leader-aware queue consumer
/// (<see cref="LeaderAwareEntityQueueBackgroundService{TEntity}"/>) uses these methods so that a former
/// leader leaves no work behind: it calls <see cref="SuspendIntake"/> + <see cref="Clear"/> when
/// leadership is lost and <see cref="ResumeIntake"/> when it is (re)acquired.
/// </para>
/// <para>
/// This is a <strong>separate, opt-in</strong> interface rather than part of
/// <see cref="ITimedEntityQueue{TEntity}"/> so that a custom queue only participates in leadership-loss
/// protection when it deliberately implements it. A queue that does not implement this interface keeps
/// working, but provides no such protection (the consumer logs a warning in that case). The built-in
/// <see cref="TimedEntityQueue{TEntity}"/> implements it.
/// </para>
/// </remarks>
public interface ISuspendableEntityQueue
{
    /// <summary>
    /// Removes all pending (ready) and scheduled entries without closing the queue for future use.
    /// </summary>
    void Clear();

    /// <summary>
    /// Suspends acceptance of new entries: subsequent enqueues are dropped until <see cref="ResumeIntake"/>
    /// is called. To be effective, the implementation must make the intake check and the scheduling mutation
    /// atomic with this method and <see cref="Clear"/> (e.g. under a shared lock).
    /// </summary>
    void SuspendIntake();

    /// <summary>
    /// Resumes acceptance of new entries after a previous <see cref="SuspendIntake"/> call.
    /// </summary>
    void ResumeIntake();
}
