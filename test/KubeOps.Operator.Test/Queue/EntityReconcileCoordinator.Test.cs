// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;

namespace KubeOps.Operator.Test.Queue;

[Trait("Area", "MultipleControllers")]
public sealed class EntityReconcileCoordinatorTest
{
    private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Should_Serialize_The_Same_Uid_Across_Callers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var coordinator = Coordinator();
        var entity = Entity();

        var first = await coordinator.AcquireEntityLockAsync(entity, ParallelReconciliationConflictStrategy.WaitForCompletion, ct);
        first.Should().NotBeNull();

        var second = coordinator.AcquireEntityLockAsync(entity, ParallelReconciliationConflictStrategy.WaitForCompletion, ct);

        // The second caller must wait while the first still holds the lock.
        (await Task.WhenAny(second, Task.Delay(ShortWait, ct))).Should().NotBe(second);

        await first!.DisposeAsync();

        var secondLock = await second;
        secondLock.Should().NotBeNull();
        await secondLock!.DisposeAsync();
    }

    [Fact]
    public async Task Should_Return_Null_On_Contention_For_NonBlocking_Strategy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var coordinator = Coordinator();
        var entity = Entity();

        var held = await coordinator.AcquireEntityLockAsync(entity, ParallelReconciliationConflictStrategy.Discard, ct);
        held.Should().NotBeNull();

        var contended = await coordinator.AcquireEntityLockAsync(entity, ParallelReconciliationConflictStrategy.Discard, ct);
        contended.Should().BeNull();

        await held!.DisposeAsync();
    }

    [Fact]
    public async Task Should_Cap_Parallel_Slots_At_The_Configured_Maximum()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var coordinator = Coordinator(maxParallel: 2);

        await coordinator.AcquireParallelSlotAsync(ct);
        await coordinator.AcquireParallelSlotAsync(ct);

        var third = coordinator.AcquireParallelSlotAsync(ct);
        (await Task.WhenAny(third, Task.Delay(ShortWait, ct))).Should().NotBe(third);

        coordinator.ReleaseParallelSlot();
        await third; // a freed slot lets the third caller proceed

        coordinator.ReleaseParallelSlot();
        coordinator.ReleaseParallelSlot();
    }

    [Fact]
    public async Task Should_Not_Release_The_Lock_When_A_Uid_Wait_Is_Cancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var coordinator = Coordinator();
        var entity = Entity();

        var held = await coordinator.AcquireEntityLockAsync(entity, ParallelReconciliationConflictStrategy.WaitForCompletion, ct);
        held.Should().NotBeNull();

        using var cts = new CancellationTokenSource();
        var cancelledWait = coordinator.AcquireEntityLockAsync(entity, ParallelReconciliationConflictStrategy.WaitForCompletion, cts.Token);
        await cts.CancelAsync();

        var act = async () => await cancelledWait;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // The cancelled wait must not have released the still-held lock: a non-blocking acquire is contended.
        var contended = await coordinator.AcquireEntityLockAsync(entity, ParallelReconciliationConflictStrategy.Discard, ct);
        contended.Should().BeNull();

        // And the bookkeeping is intact: after release the lock is acquirable again.
        await held!.DisposeAsync();
        var afterRelease = await coordinator.AcquireEntityLockAsync(entity, ParallelReconciliationConflictStrategy.Discard, ct);
        afterRelease.Should().NotBeNull();
        await afterRelease!.DisposeAsync();
    }

    private static EntityReconcileCoordinator<V1OperatorIntegrationTestEntity> Coordinator(int maxParallel = 4) =>
        new(new OperatorSettingsBuilder
        {
            ParallelReconciliation = new() { MaxParallelReconciliations = maxParallel },
        }.Build());

    private static V1OperatorIntegrationTestEntity Entity(string uid = "uid-1") =>
        new() { Metadata = new() { Name = "entity", Uid = uid } };
}
