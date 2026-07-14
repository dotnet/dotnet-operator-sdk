// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.LeaderElection;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Logging;
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

    public ScopeAwareEntityQueueBackgroundServiceTest()
    {
        _reconciler
            .Setup(r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1OperatorIntegrationTestEntity>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReconciliationContext<V1OperatorIntegrationTestEntity> ctx, CancellationToken _) =>
                ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(ctx.Entity));
    }

    [Fact]
    public async Task Should_Reconcile_On_Current_Object_When_Snapshot_Was_Responsible_But_Current_Is_Not()
    {
        // The snapshot enqueued while responsible; responsibility moved away before the entry ran. The gate
        // must see the freshly loaded object (not responsible) and skip - the stale snapshot must not win.
        var snapshot = CreateEntity("snapshot");
        var current = CreateEntity("current");
        SetupLoad(snapshot, current);
        SetupResponsibility(snapshot, responsible: true);
        SetupResponsibility(current, responsible: false);
        await using var service = CreateService();

        var result = await service.InvokeReconcileSingleAsync(
            CreateEntry(snapshot, ReconciliationType.Modified),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _reconciler.Verify(
            r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1OperatorIntegrationTestEntity>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_Reconcile_Current_Object_When_Snapshot_Was_Not_Responsible_But_Current_Is()
    {
        // The snapshot was not this instance's when enqueued, but responsibility moved here before it ran.
        // The gate on the current object must allow it, and the reconciler must receive the current object.
        var snapshot = CreateEntity("snapshot");
        var current = CreateEntity("current");
        SetupLoad(snapshot, current);
        SetupResponsibility(snapshot, responsible: false);
        SetupResponsibility(current, responsible: true);
        await using var service = CreateService();

        var result = await service.InvokeReconcileSingleAsync(
            CreateEntry(snapshot, ReconciliationType.Modified),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _reconciler.Verify(
            r => r.Reconcile(
                It.Is<ReconciliationContext<V1OperatorIntegrationTestEntity>>(c => ReferenceEquals(c.Entity, current)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_Decide_Deleted_Entry_On_The_Delete_Snapshot()
    {
        // For Deleted there is no current object to load; the decision is made on the delete snapshot.
        var snapshot = CreateEntity("snapshot");
        SetupResponsibility(snapshot, responsible: true);
        await using var service = CreateService();

        var result = await service.InvokeReconcileSingleAsync(
            CreateEntry(snapshot, ReconciliationType.Deleted),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _client.Verify(
            c => c.GetAsync<V1OperatorIntegrationTestEntity>(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _reconciler.Verify(
            r => r.Reconcile(
                It.Is<ReconciliationContext<V1OperatorIntegrationTestEntity>>(c => ReferenceEquals(c.Entity, snapshot)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_Skip_Deleted_Entry_When_Not_Responsible_For_The_Delete_Snapshot()
    {
        var snapshot = CreateEntity("snapshot");
        SetupResponsibility(snapshot, responsible: false);
        await using var service = CreateService();

        var result = await service.InvokeReconcileSingleAsync(
            CreateEntry(snapshot, ReconciliationType.Deleted),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _reconciler.Verify(
            r => r.Reconcile(
                It.IsAny<ReconciliationContext<V1OperatorIntegrationTestEntity>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupLoad(V1OperatorIntegrationTestEntity snapshot, V1OperatorIntegrationTestEntity current) =>
        _client
            .Setup(c => c.GetAsync<V1OperatorIntegrationTestEntity>(
                snapshot.Name(),
                snapshot.Namespace(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);

    private void SetupResponsibility(V1OperatorIntegrationTestEntity entity, bool responsible) =>
        _scope
            .Setup(s => s.IsResponsibleForAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responsible);

    private static V1OperatorIntegrationTestEntity CreateEntity(string uid) =>
        new()
        {
            Metadata = new V1ObjectMeta { Name = "test-entity", NamespaceProperty = "test-namespace", Uid = uid },
        };

    private static QueueEntry<V1OperatorIntegrationTestEntity> CreateEntry(
        V1OperatorIntegrationTestEntity entity, ReconciliationType type) =>
        new(entity, type, ReconciliationTriggerSource.ApiServer, RetryCount: 0);

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
            Mock.Of<IEntityReconcileCoordinator<V1OperatorIntegrationTestEntity>>(),
            Mock.Of<ILogger<ScopeAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>>>(),
            leadershipScope,
            Mock.Of<IEntityLoggingScopeFactory<V1OperatorIntegrationTestEntity>>())
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> InvokeReconcileSingleAsync(
            QueueEntry<V1OperatorIntegrationTestEntity> entry,
            CancellationToken cancellationToken) =>
            ReconcileSingleAsync(entry, cancellationToken);
    }
}
