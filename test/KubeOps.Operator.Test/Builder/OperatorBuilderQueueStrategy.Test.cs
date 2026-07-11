// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Moq;

namespace KubeOps.Operator.Test.Builder;

public sealed class OperatorBuilderQueueStrategyTest
{
    [Fact]
    public void Should_Register_BackgroundService_For_InMemory_Strategy()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { QueueStrategy = QueueStrategy.InMemory }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        // The queue is owned by the pipeline (not part of the service collection); the consumer is
        // factory-registered per pipeline.
        builder.Services.Should().NotContain(s =>
            s.ServiceType == typeof(ITimedEntityQueue<V1OperatorIntegrationTestEntity>));

        var hostedServices = ResolveHostedServices(builder);
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    [Trait("Area", "LeaderLoss")]
    public void Should_Register_LeaderAware_BackgroundService_For_Single_LeaderElection()
    {
        var builder = new OperatorBuilder(
            new ServiceCollection(),
            new OperatorSettingsBuilder
            {
                QueueStrategy = QueueStrategy.InMemory,
                LeaderElectionType = LeaderElectionType.Single,
            }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        var hostedServices = ResolveHostedServices(builder);
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(LeaderAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>));
        hostedServices.Should().NotContain(s => s.GetType() == typeof(EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    public void Should_Not_Register_TimedEntityQueue_Or_BackgroundService_For_Custom_Strategy()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { QueueStrategy = QueueStrategy.Custom }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        builder.Services.Should().NotContain(s =>
            s.ServiceType == typeof(ITimedEntityQueue<V1OperatorIntegrationTestEntity>));

        // Only the watcher remains as a per-pipeline hosted service; the consumer is user-supplied.
        builder.Services
            .Count(s => s.ServiceType == typeof(IHostedService) && s.ImplementationFactory is not null)
            .Should().Be(1);
    }

    [Fact]
    public void Should_Always_Register_EntityQueue_Delegate_Regardless_Of_Strategy()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { QueueStrategy = QueueStrategy.Custom }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(EntityQueue<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public async Task Should_Route_EntityQueue_Delegate_To_Custom_Queue_For_Custom_Strategy()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { QueueStrategy = QueueStrategy.Custom }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        var customQueue = new Mock<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>();
        builder.Services.AddSingleton(customQueue.Object);

        using var provider = BuildResolvableProvider(builder);
        var entityQueue = provider.GetRequiredService<EntityQueue<V1OperatorIntegrationTestEntity>>();

        var entity = new V1OperatorIntegrationTestEntity { Metadata = new() { Name = "test", Uid = "uid" } };
        await entityQueue(
            entity,
            ReconciliationType.Modified,
            ReconciliationTriggerSource.Operator,
            TimeSpan.Zero,
            0,
            TestContext.Current.CancellationToken);

        customQueue.Verify(
            q => q.Enqueue(
                entity,
                ReconciliationType.Modified,
                ReconciliationTriggerSource.Operator,
                TimeSpan.Zero,
                0,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Area", "MultipleControllers")]
    public void Should_Register_Reconciler_For_Custom_Strategy_So_A_Custom_Consumer_Resolves()
    {
        // Regression: with QueueStrategy.Custom under normal (non-custom) leader election the user supplies
        // the queue and a consumer deriving from EntityQueueBackgroundService<TEntity>, whose constructor
        // resolves IReconciler<TEntity> from the container. IReconciler<TEntity> must therefore be
        // registered — previously it was only registered for LeaderElectionType.Custom, so building the
        // consumer failed at host startup.
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { QueueStrategy = QueueStrategy.Custom }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        builder.Services.AddSingleton(Mock.Of<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>());
        builder.Services.AddHostedService<EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>>();

        using var provider = BuildResolvableProvider(builder);

        provider.GetService<IReconciler<V1OperatorIntegrationTestEntity>>().Should().NotBeNull();

        provider.GetServices<IHostedService>()
            .Should().Contain(s => s.GetType() == typeof(EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    [Trait("Area", "MultipleControllers")]
    public void Should_Reject_Multiple_Controllers_Per_Entity_For_Custom_Queue_Strategy()
    {
        // The custom paths share a single user-owned queue and one unkeyed reconciler with no per-pipeline
        // identity, so a second controller would silently never fire (issue #909). Registration must reject
        // it instead of accepting it silently.
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { QueueStrategy = QueueStrategy.Custom }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        var act = () => builder.AddController<SecondTestController, V1OperatorIntegrationTestEntity>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*only supported with the default*");
    }

    [Fact]
    [Trait("Area", "MultipleControllers")]
    public void Should_Reject_Multiple_Controllers_Per_Entity_For_Custom_Leader_Election()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { LeaderElectionType = LeaderElectionType.Custom }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        var act = () => builder.AddController<SecondTestController, V1OperatorIntegrationTestEntity>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*only supported with the default*");
    }

    private static List<IHostedService> ResolveHostedServices(OperatorBuilder builder)
    {
        using var provider = BuildResolvableProvider(builder);
        return provider.GetServices<IHostedService>().ToList();
    }

    private static ServiceProvider BuildResolvableProvider(OperatorBuilder builder)
    {
        builder.Services.AddLogging();
        builder.Services.Replace(ServiceDescriptor.Singleton(Mock.Of<IKubernetesClient>()));
        builder.Services.Replace(ServiceDescriptor.Singleton(Mock.Of<IKubernetes>()));
        return builder.Services.BuildServiceProvider();
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

    private sealed class SecondTestController : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }
}
