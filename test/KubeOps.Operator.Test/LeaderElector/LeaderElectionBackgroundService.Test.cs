// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.LeaderElection;

using KubeOps.Operator.LeaderElection;

using Microsoft.Extensions.Logging;

using Moq;

namespace KubeOps.Operator.Test.LeaderElector;

public sealed class LeaderElectionBackgroundServiceTest
{
    [Fact]
    public async Task Elector_Throws_Should_Retry()
    {
        // Arrange.
        var logger = Mock.Of<ILogger<LeaderElectionBackgroundService>>();

        var electionLock = Mock.Of<ILock>();

        var electionLockSubsequentCallEvent = new AutoResetEvent(false);
        bool hasElectionLockThrown = false;
        Mock.Get(electionLock)
            .Setup(electionLock => electionLock.GetAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(
                async cancellationToken =>
                {
                    if (hasElectionLockThrown)
                    {
                        // Signal to the test that a subsequent call has been made.
                        electionLockSubsequentCallEvent.Set();

                        // Delay returning for a long time, allowing the test to stop the background service, in turn cancelling the cancellation token.
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                        throw new InvalidOperationException();
                    }

                    hasElectionLockThrown = true;
                    throw new Exception("Unit test exception");
                });

        var leaderElectionConfig = new LeaderElectionConfig(electionLock);
        var leaderElector = new k8s.LeaderElection.LeaderElector(leaderElectionConfig);

        var leaderElectionBackgroundService = new LeaderElectionBackgroundService(logger, leaderElector);

        // Act / Assert.
        await leaderElectionBackgroundService.StartAsync(CancellationToken.None);

        // Starting the background service should result in the lock attempt throwing, and then a subsequent attempt being made.
        // Wait for the subsequent event to be signalled, if we time out the test fails. The retry delay requires us to wait at least 3 seconds.
        electionLockSubsequentCallEvent.WaitOne(TimeSpan.FromMilliseconds(3100)).Should().BeTrue();

        await leaderElectionBackgroundService.StopAsync(CancellationToken.None);
    }
}
