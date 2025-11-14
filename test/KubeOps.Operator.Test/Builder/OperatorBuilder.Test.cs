// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Events;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.KubernetesClient.LabelSelectors;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Builder;

public sealed class OperatorBuilderTest
{
    private readonly IOperatorBuilder _builder = new OperatorBuilder(new ServiceCollection(), new());

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
            s.ServiceType == typeof(IEntityController<V1OperatorIntegrationTestEntity>) &&
            s.ImplementationType == typeof(TestController) &&
            s.Lifetime == ServiceLifetime.Scoped);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(ResourceWatcher<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Singleton);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(ITimedEntityQueue<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Singleton);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(EntityRequeue<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void Should_Add_Controller_Resources_With_Label_Selector()
    {
        _builder.AddController<TestController, V1OperatorIntegrationTestEntity, TestLabelSelector>();

        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IEntityController<V1OperatorIntegrationTestEntity>) &&
            s.ImplementationType == typeof(TestController) &&
            s.Lifetime == ServiceLifetime.Scoped);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(ResourceWatcher<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Singleton);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(ITimedEntityQueue<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Singleton);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(EntityRequeue<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Transient);
        _builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IEntityLabelSelector<V1OperatorIntegrationTestEntity>) &&
            s.ImplementationType == typeof(TestLabelSelector) &&
            s.Lifetime == ServiceLifetime.Singleton);
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
        var builder = new OperatorBuilder(new ServiceCollection(), new() { LeaderElectionType = LeaderElectionType.Single });
        builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(k8s.LeaderElection.LeaderElector) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Should_Add_LeaderAwareResourceWatcher()
    {
        var builder = new OperatorBuilder(new ServiceCollection(), new() { LeaderElectionType = LeaderElectionType.Single });
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(LeaderAwareResourceWatcher<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Singleton);
        builder.Services.Should().NotContain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(ResourceWatcher<V1OperatorIntegrationTestEntity>) &&
            s.Lifetime == ServiceLifetime.Singleton);
    }

    private sealed class TestController : IEntityController<V1OperatorIntegrationTestEntity>
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
                new EqualsSelector("label", "value")
            };

            return ValueTask.FromResult<string?>(labelSelectors.ToExpression());
        }
    }
}
