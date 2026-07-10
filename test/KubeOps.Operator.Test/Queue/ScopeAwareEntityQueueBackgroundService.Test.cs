// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.LeaderElection;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.Logging;

using Moq;

namespace KubeOps.Operator.Test.Queue;

[Trait("Area", "ScopedLeaderElection")]
public sealed class ScopeAwareEntityQueueBackgroundServiceTest
{
    private readonly Mock<ILeadershipScope> _scope = new();
    private readonly Mock<IReconciler<V1OperatorIntegrationTestEntity>> _reconciler = new();
    private readonly Mock<IKubernetesClient> _client = new();

    [Fact]
    public async Task Should_Skip_Entry_When_Not_Responsible_For_Namespace()
    {
        _scope
            .Setup(s => s.IsResponsibleForAsync("foreign-namespace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        await using var service = CreateService();

        var result = await service.InvokeReconcileSingleAsync(
            CreateEntry("foreign-namespace"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _reconciler.Verify(
            r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1OperatorIntegrationTestEntity>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(
            c => c.GetAsync<V1OperatorIntegrationTestEntity>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_Reconcile_Entry_When_Responsible_For_Namespace()
    {
        var entry = CreateEntry("owned-namespace");
        _scope
            .Setup(s => s.IsResponsibleForAsync("owned-namespace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _client
            .Setup(c => c.GetAsync<V1OperatorIntegrationTestEntity>(
                entry.Entity.Name(),
                "owned-namespace",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry.Entity);
        _reconciler
            .Setup(r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1OperatorIntegrationTestEntity>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entry.Entity));
        await using var service = CreateService();

        var result = await service.InvokeReconcileSingleAsync(entry, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _reconciler.Verify(
            r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1OperatorIntegrationTestEntity>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static QueueEntry<V1OperatorIntegrationTestEntity> CreateEntry(string @namespace)
        => new(
            new V1OperatorIntegrationTestEntity
            {
                Metadata = new V1ObjectMeta { Name = "test-entity", NamespaceProperty = @namespace, Uid = "uid-1" },
            },
            ReconciliationType.Modified,
            ReconciliationTriggerSource.ApiServer,
            RetryCount: 0);

    private TestableService CreateService()
        => new(
            _client.Object,
            new OperatorSettingsBuilder().Build(),
            _reconciler.Object,
            _scope.Object);

    private sealed class TestableService(
        IKubernetesClient client,
        OperatorSettings settings,
        IReconciler<V1OperatorIntegrationTestEntity> reconciler,
        ILeadershipScope leadershipScope)
        : ScopeAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>(
            new ActivitySource("test"),
            client,
            settings,
            Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>(),
            reconciler,
            Mock.Of<ILogger<ScopeAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>>>(),
            leadershipScope)
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> InvokeReconcileSingleAsync(
            QueueEntry<V1OperatorIntegrationTestEntity> entry,
            CancellationToken cancellationToken)
            => ReconcileSingleAsync(entry, cancellationToken);
    }
}
