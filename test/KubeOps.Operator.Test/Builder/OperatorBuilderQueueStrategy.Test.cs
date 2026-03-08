// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Builder;

public sealed class OperatorBuilderQueueStrategyTest
{
    [Fact]
    public void Should_Register_TimedEntityQueue_And_BackgroundService_For_InMemory_Strategy()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new() { QueueStrategy = QueueStrategy.InMemory });
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(ITimedEntityQueue<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Singleton);
        builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    public void Should_Not_Register_TimedEntityQueue_Or_BackgroundService_For_Custom_Strategy()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new() { QueueStrategy = QueueStrategy.Custom });
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        builder.Services.Should().NotContain(s =>
            s.ServiceType == typeof(ITimedEntityQueue<V1OperatorIntegrationTestEntity>));
        builder.Services.Should().NotContain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    public void Should_Always_Register_EntityQueue_Delegate_Regardless_Of_Strategy()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new() { QueueStrategy = QueueStrategy.Custom });
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(EntityQueue<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Transient);
    }

    private sealed class TestController : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }
}
