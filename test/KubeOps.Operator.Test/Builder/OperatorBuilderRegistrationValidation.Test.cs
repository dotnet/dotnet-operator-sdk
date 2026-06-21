// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

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

    // With the in-memory strategy the SDK registers the queue, watcher and (for None/Single) the consumer,
    // so None/Single validate as complete. The custom queue strategy leaves the queue itself unregistered
    // (the SDK only auto-registers it for in-memory), so SDK-only registrations are incomplete and must
    // fail. Custom leader election additionally leaves watcher and consumer to the user.
    [Theory]
    [InlineData(LeaderElectionType.None, QueueStrategy.InMemory, true)]
    [InlineData(LeaderElectionType.None, QueueStrategy.Custom, false)]
    [InlineData(LeaderElectionType.Single, QueueStrategy.InMemory, true)]
    [InlineData(LeaderElectionType.Single, QueueStrategy.Custom, false)]
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
            .And.Contain("IEntityQueueConsumer")
            .And.Contain("LeaderElectionType.Custom")
            .And.Contain("QueueStrategy.InMemory");
    }

    [Fact]
    public async Task Should_Fail_When_Custom_Queue_Strategy_Does_Not_Register_A_Queue()
    {
        // The watcher and reconciler take ITimedEntityQueue<TEntity> as a constructor dependency, so it is
        // required even with QueueStrategy.Custom (where the SDK does not register it). A missing queue would
        // otherwise only fail at host startup with a DI error. Use None leader election so the queue is the
        // only gap.
        var validator = CreateValidatorForSdkRegistrations(LeaderElectionType.None, QueueStrategy.Custom);

        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should()
            .Contain(nameof(V1OperatorIntegrationTestEntity))
            .And.Contain("ITimedEntityQueue");
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
    public async Task Should_Pass_For_Custom_LeaderElection_With_Custom_Queue_When_Watcher_Queue_And_Consumer_Are_Registered()
    {
        // With QueueStrategy.Custom the user supplies the queue (still required, since watcher and reconciler
        // depend on it) and the consumer (recognised via IEntityQueueConsumer<TEntity>). With Custom leader
        // election the suspendable capability is not required.
        var validator = CreateValidator(
            LeaderElectionType.Custom,
            QueueStrategy.Custom,
            services =>
            {
                services.AddHostedService<CustomWatcher>();
                services.AddSingleton<ITimedEntityQueue<V1OperatorIntegrationTestEntity>, NonSuspendableQueue>();
                services.AddHostedService<CustomConsumer>();
            });

        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Should_Fail_When_Queue_Consumer_Is_Missing()
    {
        // QueueStrategy.Custom + a registered queue but no consumer: nothing drains the queue. The consumer
        // is recognised via IEntityQueueConsumer<TEntity>, so its absence is caught. Use None leader election
        // (the SDK registers the watcher) and supply only the queue, so the consumer is the only gap.
        var validator = CreateValidator(
            LeaderElectionType.None,
            QueueStrategy.Custom,
            services => services.AddSingleton<ITimedEntityQueue<V1OperatorIntegrationTestEntity>, NonSuspendableQueue>());

        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should()
            .Contain(nameof(V1OperatorIntegrationTestEntity))
            .And.Contain("IEntityQueueConsumer");
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

    [Fact]
    [Trait("Area", "LeaderLoss")]
    public async Task Should_Fail_When_Single_LeaderElection_Queue_Lacks_Suspendable_Capability()
    {
        // LeaderElectionType.Single requires the queue to support the leadership gate
        // (ISuspendableEntityQueue). A custom queue without it would silently lose leadership-loss
        // protection, so validation must reject it.
        var settings = new OperatorSettingsBuilder
        {
            LeaderElectionType = LeaderElectionType.Single,
            QueueStrategy = QueueStrategy.Custom,
            ValidateRegistrations = true,
        }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        // A leadership-aware consumer is present, so the only gap is the non-suspendable queue.
        services.AddSingleton<ITimedEntityQueue<V1OperatorIntegrationTestEntity>, NonSuspendableQueue>();
        services.AddHostedService<CustomLeaderAwareConsumer>();

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should()
            .Contain(nameof(V1OperatorIntegrationTestEntity))
            .And.Contain(nameof(ISuspendableEntityQueue));
    }

    [Fact]
    [Trait("Area", "LeaderLoss")]
    public async Task Should_Fail_When_Single_LeaderElection_Consumer_Is_Not_Leader_Aware()
    {
        // The queue supports the gate (ISuspendableEntityQueue), but a plain consumer never drives it, so
        // leadership-loss protection would not actually take effect. Validation must reject it under Single.
        var settings = new OperatorSettingsBuilder
        {
            LeaderElectionType = LeaderElectionType.Single,
            QueueStrategy = QueueStrategy.Custom,
            ValidateRegistrations = true,
        }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        services.AddSingleton<ITimedEntityQueue<V1OperatorIntegrationTestEntity>, SuspendableQueue>();
        services.AddHostedService<CustomConsumer>();

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should()
            .Contain(nameof(V1OperatorIntegrationTestEntity))
            .And.Contain(nameof(ILeaderAwareEntityQueueConsumer<V1OperatorIntegrationTestEntity>));
    }

    [Fact]
    [Trait("Area", "LeaderLoss")]
    public async Task Should_Pass_For_Single_LeaderElection_With_Suspendable_Queue_And_LeaderAware_Consumer()
    {
        var settings = new OperatorSettingsBuilder
        {
            LeaderElectionType = LeaderElectionType.Single,
            QueueStrategy = QueueStrategy.Custom,
            ValidateRegistrations = true,
        }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        services.AddSingleton<ITimedEntityQueue<V1OperatorIntegrationTestEntity>, SuspendableQueue>();
        services.AddHostedService<CustomLeaderAwareConsumer>();

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Area", "LeaderLoss")]
    public async Task Should_Recognize_Open_Generic_Queue_Registration()
    {
        // A queue registered as an open generic (AddSingleton(typeof(ITimedEntityQueue<>), typeof(...<>)))
        // is resolvable by the DI container, so the validator must recognise both its presence and its
        // ISuspendableEntityQueue capability under Single.
        var settings = new OperatorSettingsBuilder
        {
            LeaderElectionType = LeaderElectionType.Single,
            QueueStrategy = QueueStrategy.Custom,
            ValidateRegistrations = true,
        }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        services.AddSingleton(typeof(ITimedEntityQueue<>), typeof(OpenSuspendableQueue<>));
        services.AddHostedService<CustomLeaderAwareConsumer>();

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Area", "LeaderLoss")]
    public async Task Should_Fail_When_Single_LeaderElection_Queue_Is_Factory_Registered()
    {
        // A factory-registered queue is resolvable, but its concrete type cannot be inspected, so the gate
        // capability cannot be verified. Under Single that must fail (consistent with the consumer), instead
        // of silently assuming the queue is gateable.
        var settings = new OperatorSettingsBuilder
        {
            LeaderElectionType = LeaderElectionType.Single,
            QueueStrategy = QueueStrategy.Custom,
            ValidateRegistrations = true,
        }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        services.AddSingleton<ITimedEntityQueue<V1OperatorIntegrationTestEntity>>(_ => new SuspendableQueue());
        services.AddHostedService<CustomLeaderAwareConsumer>();

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should()
            .Contain(nameof(V1OperatorIntegrationTestEntity))
            .And.Contain("factory")
            .And.Contain(nameof(ISuspendableEntityQueue));
    }

    [Fact]
    public async Task Should_Report_Missing_Queue_When_Registered_As_Keyed_Service()
    {
        // A keyed queue registration does not satisfy the unkeyed constructor dependency of the watcher and
        // reconciler, so the validator must not count it as a registered queue.
        var settings = new OperatorSettingsBuilder
        {
            LeaderElectionType = LeaderElectionType.None,
            QueueStrategy = QueueStrategy.Custom,
            ValidateRegistrations = true,
        }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        services.AddKeyedSingleton<ITimedEntityQueue<V1OperatorIntegrationTestEntity>, NonSuspendableQueue>("queue-key");
        services.AddHostedService<CustomConsumer>();

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should()
            .Contain(nameof(V1OperatorIntegrationTestEntity))
            .And.Contain("ITimedEntityQueue");
    }

    [Fact]
    public async Task Should_Report_Missing_Consumer_When_Registered_Via_Factory()
    {
        // Documented limitation: validation recognises components by their registered implementation type.
        // A consumer registered through a DI factory delegate exposes no type, so it is reported as missing.
        // Users must register the consumer with a concrete type (or disable validation).
        var settings = new OperatorSettingsBuilder
        {
            LeaderElectionType = LeaderElectionType.None,
            QueueStrategy = QueueStrategy.Custom,
            ValidateRegistrations = true,
        }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        services.AddSingleton<ITimedEntityQueue<V1OperatorIntegrationTestEntity>, NonSuspendableQueue>();
        services.AddHostedService(_ => new CustomConsumer());

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should().Contain("IEntityQueueConsumer");
    }

    [Fact]
    public async Task Should_Report_Missing_Queue_When_Open_Generic_Cannot_Close_For_Entity()
    {
        // An open-generic queue whose generic constraints exclude the managed entity is registered. The DI
        // container cannot close ITimedEntityQueue<entity> from it, so it does not actually satisfy the
        // watcher/reconciler dependency. A name-only (generic type definition) match would be a false
        // positive; validation must report the queue as missing. Use None leader election so the queue is the
        // only gap.
        var settings = new OperatorSettingsBuilder
        {
            LeaderElectionType = LeaderElectionType.None,
            QueueStrategy = QueueStrategy.Custom,
            ValidateRegistrations = true,
        }.Build();
        var services = new ServiceCollection();
        var builder = new OperatorBuilder(services, settings);
        builder.AddController<TestController, V1OperatorIntegrationTestEntity>();
        services.AddSingleton(typeof(ITimedEntityQueue<>), typeof(ConstrainedOpenQueue<>));
        services.AddHostedService<CustomConsumer>();

        var validator = CreateValidator(services, settings);
        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidRegistrationException>())
            .Which.Message.Should()
            .Contain(nameof(V1OperatorIntegrationTestEntity))
            .And.Contain("ITimedEntityQueue");
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

    // A leadership-aware consumer (implements ILeaderAwareEntityQueueConsumer via the base class).
    private sealed class CustomLeaderAwareConsumer()
        : LeaderAwareEntityQueueBackgroundService<V1OperatorIntegrationTestEntity>(
            null!, null!, null!, null!, null!, null!, null!);

    // A custom queue that does NOT implement ISuspendableEntityQueue. Validation inspects the registration
    // descriptor only and never constructs it, so the members can throw.
    private sealed class NonSuspendableQueue : ITimedEntityQueue<V1OperatorIntegrationTestEntity>
    {
        public Task<bool> Enqueue(
            V1OperatorIntegrationTestEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerator<QueueEntry<V1OperatorIntegrationTestEntity>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    // An open-generic custom queue (registered as AddSingleton(typeof(ITimedEntityQueue<>), typeof(...<>))).
    private sealed class OpenSuspendableQueue<TEntity> : ITimedEntityQueue<TEntity>, ISuspendableEntityQueue
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        public Task<bool> Enqueue(
            TEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerator<QueueEntry<TEntity>> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Clear()
        {
        }

        public void SuspendIntake()
        {
        }

        public void ResumeIntake()
        {
        }

        public void Dispose()
        {
        }
    }

    // A constraint that V1OperatorIntegrationTestEntity does not satisfy, used to build an open-generic queue
    // whose implementation cannot be closed for the managed entity.
    private interface IUnsatisfiedQueueConstraint;

    // An open-generic queue whose extra constraint excludes the managed entity, so the DI container cannot
    // close ITimedEntityQueue<V1OperatorIntegrationTestEntity> from it. Validation inspects the registration
    // descriptor only and never constructs it, so the members can throw.
    private sealed class ConstrainedOpenQueue<TEntity> : ITimedEntityQueue<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>, IUnsatisfiedQueueConstraint
    {
        public Task<bool> Enqueue(
            TEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerator<QueueEntry<TEntity>> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    // A custom queue that supports the leadership gate.
    private sealed class SuspendableQueue : ITimedEntityQueue<V1OperatorIntegrationTestEntity>, ISuspendableEntityQueue
    {
        public Task<bool> Enqueue(
            V1OperatorIntegrationTestEntity entity,
            ReconciliationType type,
            ReconciliationTriggerSource reconciliationTriggerSource,
            TimeSpan queueIn,
            int retryCount,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerator<QueueEntry<V1OperatorIntegrationTestEntity>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Clear()
        {
        }

        public void SuspendIntake()
        {
        }

        public void ResumeIntake()
        {
        }

        public void Dispose()
        {
        }
    }
}
