// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.Operator.Builder;
using KubeOps.Operator.Exceptions;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Test.TestEntities;
using KubeOps.Operator.Watcher;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Moq;

namespace KubeOps.Operator.Test.Builder;

[Trait("Area", "RegistrationValidation")]
public sealed class OperatorBuilderRegistrationValidationTest
{
    [Fact]
    public void Should_Not_Register_Validator_When_Disabled()
    {
        var builder = new OperatorBuilder(
            new ServiceCollection(),
            new OperatorSettingsBuilder { ValidateRegistrations = false }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        builder.Services.Should().NotContain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(OperatorRegistrationValidator));
    }

    [Fact]
    public void Should_Register_Validator_When_Enabled()
    {
        var builder = new OperatorBuilder(
            new ServiceCollection(),
            new OperatorSettingsBuilder { ValidateRegistrations = true }.Build());
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();

        builder.Services.Should().Contain(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(OperatorRegistrationValidator));
        builder.Services.Should().Contain(s => s.ServiceType == typeof(OperatorRegistrationRegistry));
    }

    // The SDK auto-registers the watcher and (for the in-memory strategy) the queue consumer for None and
    // Single leader election, so validation passes. With Custom leader election those components are the
    // user's responsibility; left unregistered, validation must fail.
    [Theory]
    [InlineData(LeaderElectionType.None, QueueStrategy.InMemory, true)]
    [InlineData(LeaderElectionType.None, QueueStrategy.Custom, true)]
    [InlineData(LeaderElectionType.Single, QueueStrategy.InMemory, true)]
    [InlineData(LeaderElectionType.Single, QueueStrategy.Custom, true)]
    [InlineData(LeaderElectionType.Custom, QueueStrategy.InMemory, false)]
    [InlineData(LeaderElectionType.Custom, QueueStrategy.Custom, false)]
    public async Task Should_Validate_Sdk_Registrations_For_All_Configuration_Combinations(
        LeaderElectionType leaderElectionType, QueueStrategy queueStrategy, bool expectValid)
    {
        var validator = CreateValidatorForSdkRegistrations(leaderElectionType, queueStrategy);

        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        if (expectValid)
        {
            await act.Should().NotThrowAsync();
        }
        else
        {
            await act.Should().ThrowAsync<InvalidRegistrationException>();
        }
    }

    [Fact]
    public async Task Should_Report_Missing_Watcher_And_Consumer_For_Custom_LeaderElection()
    {
        var validator = CreateValidatorForSdkRegistrations(LeaderElectionType.Custom, QueueStrategy.InMemory);

        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should()
            .Contain(nameof(V1OperatorIntegrationTestEntity))
            .And.Contain("ResourceWatcher")
            .And.Contain("EntityQueueBackgroundService")
            .And.Contain("LeaderElectionType.Custom")
            .And.Contain("QueueStrategy.InMemory");
    }

    [Fact]
    public async Task Should_Pass_For_Custom_LeaderElection_When_Watcher_And_Consumer_Are_Registered()
    {
        var validator = CreateValidator(
            LeaderElectionType.Custom,
            QueueStrategy.InMemory,
            services =>
            {
                services.AddHostedService<CustomWatcher>();
                services.AddHostedService<CustomConsumer>();
            });

        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Should_Pass_For_Custom_LeaderElection_With_Custom_Queue_When_Watcher_Is_Registered()
    {
        // With QueueStrategy.Custom the queue is user-owned and not validated, so a registered watcher is
        // sufficient.
        var validator = CreateValidator(
            LeaderElectionType.Custom,
            QueueStrategy.Custom,
            services => services.AddHostedService<CustomWatcher>());

        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Should_Fail_When_Finalizer_Is_Registered_Without_A_Controller()
    {
        // A finalizer added without a controller can never run: there is no reconciliation pipeline for
        // the entity.
        var settings = new OperatorSettingsBuilder { ValidateRegistrations = true }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddFinalizer<TestFinalizer, V1OperatorIntegrationTestEntity>("test.finalizer");

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should()
            .Contain(nameof(V1OperatorIntegrationTestEntity))
            .And.Contain("finalizer")
            .And.Contain("AddController");
    }

    [Fact]
    public async Task Should_Pass_When_Finalizer_And_Controller_Are_Registered()
    {
        var settings = new OperatorSettingsBuilder { ValidateRegistrations = true }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        builder.AddFinalizer<TestFinalizer, V1OperatorIntegrationTestEntity>("test.finalizer");

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    private static OperatorRegistrationValidator CreateValidatorForSdkRegistrations(
        LeaderElectionType leaderElectionType, QueueStrategy queueStrategy)
        => CreateValidator(leaderElectionType, queueStrategy);

    private static OperatorRegistrationValidator CreateValidator(
        LeaderElectionType leaderElectionType,
        QueueStrategy queueStrategy,
        Action<IServiceCollection>? registerUserComponents = null)
    {
        var settings = new OperatorSettingsBuilder
        {
            LeaderElectionType = leaderElectionType,
            QueueStrategy = queueStrategy,
            ValidateRegistrations = true,
        }.Build();

        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        registerUserComponents?.Invoke(services);

        return CreateValidator(services, settings);
    }

    private static OperatorRegistrationValidator CreateValidator(IServiceCollection services, OperatorSettings settings)
    {
        var registry = (OperatorRegistrationRegistry)services
            .Single(d => d.ServiceType == typeof(OperatorRegistrationRegistry))
            .ImplementationInstance!;

        return new OperatorRegistrationValidator(
            registry, settings, Mock.Of<ILogger<OperatorRegistrationValidator>>());
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

    private sealed class TestFinalizer : IEntityFinalizer<V1OperatorIntegrationTestEntity>
    {
        public Task<ReconciliationResult<V1OperatorIntegrationTestEntity>> FinalizeAsync(
            V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1OperatorIntegrationTestEntity>.Success(entity));
    }

    // Test doubles for a user-provided Custom watcher / queue consumer. Validation inspects the service
    // descriptors only (it never constructs these), so the null base arguments are never dereferenced.
    private sealed class CustomWatcher() : ResourceWatcher<V1OperatorIntegrationTestEntity>(
        null!, null!, null!, null!, null!, null!, null!, null!);

    private sealed class CustomConsumer() : EntityQueueBackgroundService<V1OperatorIntegrationTestEntity>(
        null!, null!, null!, null!, null!, null!);
}
