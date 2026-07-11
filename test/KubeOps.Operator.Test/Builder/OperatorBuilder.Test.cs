// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Crds;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Events;
using KubeOps.Abstractions.LeaderElection;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.KubernetesClient;
using KubeOps.KubernetesClient.Selectors;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Moq;

namespace KubeOps.Operator.Test.Builder;

public sealed class OperatorBuilderTest
{
    private readonly IOperatorBuilder _builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder().Build());

    [Fact]
    public void Should_Add_Default_Resources()
    {
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(OperatorSettings) &&
            s.Lifetime == ServiceLifetime.Singleton);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(EventPublisher) &&
            s.Lifetime == ServiceLifetime.Transient);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IEntityLabelSelector<>) &&
            s.ImplementationType == typeof(DefaultEntityLabelSelector<>) &&
            s.Lifetime == ServiceLifetime.Singleton);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IEntityFieldSelector<>) &&
            s.ImplementationType == typeof(DefaultEntityFieldSelector<>) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Should_Use_Specific_EntityLabelSelector_Implementation()
    {
        var services = new ServiceCollection();

        // Register the default and specific implementations
        services.AddSingleton(typeof(IEntityLabelSelector<>), typeof(DefaultEntityLabelSelector<>));
        services.TryAddSingleton<IEntityLabelSelector<V1OperatorIntegrationTestEntity>, TestLabelSelector>();

        var serviceProvider = services.BuildServiceProvider();

        var resolvedService = serviceProvider.GetRequiredService<IEntityLabelSelector<V1OperatorIntegrationTestEntity>>();

        Assert.IsType<TestLabelSelector>(resolvedService);
    }

    [Fact]
    public void Should_Add_Controller_Resources()
    {
        _builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(TestController) &&
            s.Lifetime == ServiceLifetime.Scoped);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IEntityController<V1OperatorIntegrationTestEntity>) &&
            s.ImplementationType == typeof(TestController) &&
            s.Lifetime == ServiceLifetime.Scoped);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(ControllerPipeline<V1OperatorIntegrationTestEntity>) &&
            s.ImplementationInstance is ControllerPipeline<V1OperatorIntegrationTestEntity> &&
            s.Lifetime == ServiceLifetime.Singleton);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(ActivePipelineQueue<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Scoped);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(EntityQueue<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Transient);

        // Watcher and queue consumer are created per pipeline through factory registrations; the queue
        // itself is owned by the pipeline and no longer part of the service collection.
        _builder.Services
            .Count(s => s.ServiceType == typeof(IHostedService) && s.ImplementationFactory is not null)
            .Should().Be(2);
        _builder.Services.Should().NotContain(s =>
            s.ServiceType == typeof(ITimedEntityQueue<V1OperatorIntegrationTestEntity>));

        var provider = BuildResolvableProvider(_builder);
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(ResourceWatcher<V1OperatorIntegrationTestEntity>));
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    public void Should_Add_Controller_Resources_With_Label_Selector()
    {
        _builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(TestController) &&
            s.Lifetime == ServiceLifetime.Scoped);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(TestLabelSelector) &&
            s.Lifetime == ServiceLifetime.Singleton);

        var pipeline = GetPipelines<V1OperatorIntegrationTestEntity>(_builder).Should().ContainSingle().Subject;
        pipeline.ControllerType.Should().Be<TestController>();
        pipeline.LabelSelectorType.Should().Be<TestLabelSelector>();
        pipeline.FieldSelectorType.Should().BeNull();

        // The interface mapping resolves the first registered custom selector (back-compat).
        var provider = BuildResolvableProvider(_builder);
        provider.GetRequiredService<IEntityLabelSelector<V1OperatorIntegrationTestEntity>>()
            .Should().BeOfType<TestLabelSelector>();
    }

    [Fact]
    public void Should_Add_Controller_Resources_With_Field_Selector()
    {
        _builder.AddControllerWithFieldSelector<TestController, V1OperatorIntegrationTestEntity, TestFieldSelector>();

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(TestController) &&
            s.Lifetime == ServiceLifetime.Scoped);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(TestFieldSelector) &&
            s.Lifetime == ServiceLifetime.Singleton);

        var pipeline = GetPipelines<V1OperatorIntegrationTestEntity>(_builder).Should().ContainSingle().Subject;
        pipeline.FieldSelectorType.Should().Be<TestFieldSelector>();
    }

    [Fact]
    public void Should_Support_Multiple_Controllers_For_Same_Entity()
    {
        _builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();
        _builder.AddControllerWithLabelSelector<SecondTestController, V1OperatorIntegrationTestEntity, SecondTestLabelSelector>();

        var pipelines = GetPipelines<V1OperatorIntegrationTestEntity>(_builder);
        pipelines.Should().HaveCount(2);
        pipelines.Select(p => p.CachePartition).Distinct().Should().HaveCount(2);

        // Two watchers and two queue consumers, one pair per pipeline.
        _builder.Services
            .Count(s => s.ServiceType == typeof(IHostedService) && s.ImplementationFactory is not null)
            .Should().Be(4);

        var provider = BuildResolvableProvider(_builder);
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.OfType<ResourceWatcher<V1OperatorIntegrationTestEntity>>().Should().HaveCount(2);
        hostedServices.OfType<EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>>().Should().HaveCount(2);
    }

    [Fact]
    public void Should_Throw_When_Registering_Duplicate_Controller_Pipeline()
    {
        _builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();

        var act = () =>
            _builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    [Fact]
    public void Should_Allow_Same_Controller_With_Different_Selectors()
    {
        _builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();
        _builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, SecondTestLabelSelector>();

        GetPipelines<V1OperatorIntegrationTestEntity>(_builder).Should().HaveCount(2);
    }

    [Fact]
    public void Should_Add_Finalizer_Resources()
    {
        _builder.AddFinalizer<TestFinalizer, V1OperatorIntegrationTestEntity>(string.Empty);

        _builder.Services.Should().Contain(s =>
            s.IsKeyedService &&
            s.KeyedImplementationType == typeof(TestFinalizer) &&
            s.Lifetime == ServiceLifetime.Transient);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(EntityFinalizerAttacher<TestFinalizer, V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void Should_Add_Leader_Elector()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { LeaderElectionType = LeaderElectionType.Single }.Build());
        builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(k8s.LeaderElection.LeaderElector) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Should_Add_LeaderAwareResourceWatcher()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { LeaderElectionType = LeaderElectionType.Single }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        var provider = BuildResolvableProvider(builder);
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>));
        hostedServices.Should().NotContain(s => s.GetType() == typeof(ResourceWatcher<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    public void Should_Register_Single_Shared_Watcher_For_SharedPerEntity_Strategy()
    {
        var builder = new OperatorBuilder(
            new ServiceCollection(),
            new OperatorSettingsBuilder { WatchStrategy = WatchStrategy.SharedPerEntity }.Build());
        builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();
        builder.AddControllerWithLabelSelector<SecondTestController, V1OperatorIntegrationTestEntity, SecondTestLabelSelector>();

        // One shared watcher plus two queue consumers.
        builder.Services
            .Count(s => s.ServiceType == typeof(IHostedService) && s.ImplementationFactory is not null)
            .Should().Be(3);

        var provider = BuildResolvableProvider(builder);
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(SharedResourceWatcher<V1OperatorIntegrationTestEntity>));
        hostedServices.OfType<EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>>().Should().HaveCount(2);
    }

    [Fact]
    public void Should_Use_Dedicated_Watcher_For_Single_Pipeline_In_SharedPerEntity_Strategy()
    {
        var builder = new OperatorBuilder(
            new ServiceCollection(),
            new OperatorSettingsBuilder { WatchStrategy = WatchStrategy.SharedPerEntity }.Build());
        builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();

        // With a single pipeline the shared strategy degenerates to a dedicated watcher, so the label
        // selector is applied server-side.
        var provider = BuildResolvableProvider(builder);
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(ResourceWatcher<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    public void Should_Use_Dedicated_Watcher_For_Field_Selector_Pipelines_In_SharedPerEntity_Strategy()
    {
        var builder = new OperatorBuilder(
            new ServiceCollection(),
            new OperatorSettingsBuilder { WatchStrategy = WatchStrategy.SharedPerEntity }.Build());
        builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();
        builder.AddControllerWithLabelSelector<SecondTestController, V1OperatorIntegrationTestEntity, SecondTestLabelSelector>();
        builder.AddControllerWithFieldSelector<ThirdTestController, V1OperatorIntegrationTestEntity, TestFieldSelector>();

        // Field selectors cannot be evaluated client-side: the field-selector pipeline keeps a dedicated
        // watcher next to the shared one.
        var provider = BuildResolvableProvider(builder);
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(SharedResourceWatcher<V1OperatorIntegrationTestEntity>));
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(ResourceWatcher<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    [Trait("Area", "ScopedLeaderElection")]
    public void Should_Add_ScopeAware_Services_For_Scoped_Leader_Election()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { LeaderElectionType = LeaderElectionType.Scoped }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        builder.Services.AddSingleton(Mock.Of<ILeadershipScope>());

        var provider = BuildResolvableProvider(builder);
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(ScopeAwareResourceWatcher<V1OperatorIntegrationTestEntity>));
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(ScopeAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>));
        hostedServices.Should().NotContain(s =>
            s.GetType() == typeof(ResourceWatcher<V1OperatorIntegrationTestEntity>) ||
            s.GetType() == typeof(LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    [Trait("Area", "ScopedLeaderElection")]
    public void Should_Support_Multiple_Controllers_With_ScopeAware_Shared_Watcher()
    {
        var builder = new OperatorBuilder(
            new ServiceCollection(),
            new OperatorSettingsBuilder
            {
                LeaderElectionType = LeaderElectionType.Scoped,
                WatchStrategy = WatchStrategy.SharedPerEntity,
            }.Build());
        builder.AddControllerWithLabelSelector<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();
        builder.AddControllerWithLabelSelector<SecondTestController, V1OperatorIntegrationTestEntity, SecondTestLabelSelector>();
        builder.Services.AddSingleton(Mock.Of<ILeadershipScope>());

        var provider = BuildResolvableProvider(builder);
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle(s => s.GetType() == typeof(ScopeAwareSharedResourceWatcher<V1OperatorIntegrationTestEntity>));
        hostedServices.Count(s => s.GetType() == typeof(ScopeAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>))
            .Should().Be(2);
    }

    [Fact]
    [Trait("Area", "ScopedLeaderElection")]
    public void Should_Not_Add_Leader_Elector_For_Scoped_Leader_Election()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new OperatorSettingsBuilder { LeaderElectionType = LeaderElectionType.Scoped }.Build());

        builder.Services.Should().NotContain(s =>
            s.ServiceType == typeof(k8s.LeaderElection.LeaderElector));
    }

    [Fact]
    public void Should_Add_CrdInstaller_Settings()
    {
        _builder.AddCrdInstaller(c => c
            .WithOverwriteExisting()
            .WithDeleteOnShutdown());

        var settingsDescriptor = _builder.Services.Single(s => s.ServiceType == typeof(CrdInstallerSettings));

        settingsDescriptor.ImplementationInstance.Should().BeEquivalentTo(new
        {
            OverwriteExisting = true,
            DeleteOnShutdown = true,
        });
        settingsDescriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        _builder.Services.Should().NotContain(s => s.ServiceType == typeof(CrdInstallerSettingsBuilder));
    }

    private static List<ControllerPipeline<TEntity>> GetPipelines<TEntity>(IOperatorBuilder builder)
        where TEntity : IKubernetesObject<k8s.Models.V1ObjectMeta> =>
        builder.Services
            .Where(s => s.ServiceType == typeof(ControllerPipeline<TEntity>))
            .Select(s => (ControllerPipeline<TEntity>)s.ImplementationInstance!)
            .ToList();

    // Hosted services are factory-registered per pipeline, so their types are only observable on the
    // resolved instances. The Kubernetes clients are mocked so resolution does not require a kubeconfig.
    private static ServiceProvider BuildResolvableProvider(IOperatorBuilder builder)
    {
        builder.Services.AddLogging();
        builder.Services.Replace(ServiceDescriptor.Singleton(Mock.Of<IKubernetesClient>()));
        builder.Services.Replace(ServiceDescriptor.Singleton(Mock.Of<IKubernetes>()));
        return builder.Services.BuildServiceProvider();
    }

    private sealed class TestController : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }

    private sealed class SecondTestController : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }

    private sealed class ThirdTestController : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));

        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }

    private sealed class TestFinalizer : IEntityFinalizer<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> FinalizeAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }

    private sealed class TestLabelSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken)
        {
            var labelSelectors = new LabelSelector[]
            {
                new EqualsLabelSelector("label", "value")
            };

            return ValueTask.FromResult<string?>(labelSelectors.ToExpression());
        }
    }

    private sealed class SecondTestLabelSelector : IEntityLabelSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetLabelSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("label=value,other=thing");
    }

    private sealed class TestFieldSelector : IEntityFieldSelector<V1OperatorIntegrationTestEntity>
    {
        public ValueTask<string?> GetFieldSelectorAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("metadata.name=my-resource");
    }
}
