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
using KubeOps.Abstractions.Reconciliation.Queue;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Queue;
using KubeOps.Operator.Reconciliation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Operator.Test.Reconciliation;

public sealed class ReconcilerTest
{
    private readonly Mock<ILogger<Reconciler<V1ConfigMap>>> _mockLogger;
    private readonly Mock<IFusionCacheProvider> _mockCacheProvider;
    private readonly Mock<IFusionCache> _mockCache;
    private readonly Mock<IKeyedServiceProvider> _mockServiceProvider;
    private readonly Mock<IKubernetesClient> _mockClient;
    private readonly Mock<ITimedEntityQueue<V1ConfigMap>> _mockQueue;
    private readonly OperatorSettings _settings;

    public ReconcilerTest()
    {
        _mockLogger = new();
        _mockCacheProvider = new();
        _mockCache = new();
        _mockServiceProvider = new();
        _mockClient = new();
        _mockQueue = new();
        _settings = new() { AutoAttachFinalizers = false, AutoDetachFinalizers = false };

        _mockCacheProvider
            .Setup(p => p.GetCache(It.IsAny<string>()))
            .Returns(_mockCache.Object);
    }

    [Fact]
    public async Task Reconcile_Should_Remove_Entity_From_Queue_Before_Processing()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        var controller = CreateMockController();

        var reconciler = CreateReconcilerForController(controller);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockQueue.Verify(
            q => q.Remove(entity, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_Should_Enqueue_Entity_When_Result_Has_RequeueAfter()
    {
        var entity = CreateTestEntity();
        var requeueAfter = TimeSpan.FromMinutes(5);
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);

        var controller = CreateMockController(
            reconcileResult: ReconciliationResult<V1ConfigMap>.Success(entity, requeueAfter));

        var reconciler = CreateReconcilerForController(controller);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockQueue.Verify(
            q => q.Enqueue(
                entity,
                RequeueType.Added,
                requeueAfter,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_Should_Not_Enqueue_Entity_When_Result_Has_No_RequeueAfter()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);

        var controller = CreateMockController(
            reconcileResult: ReconciliationResult<V1ConfigMap>.Success(entity));

        var reconciler = CreateReconcilerForController(controller);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockQueue.Verify(
            q => q.Enqueue(
                It.IsAny<V1ConfigMap>(),
                It.IsAny<RequeueType>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Reconcile_Should_Skip_On_Cached_Generation()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        var mockController = new Mock<IEntityController<V1ConfigMap>>();

        mockController
            .Setup(c => c.ReconcileAsync(It.IsAny<V1ConfigMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1ConfigMap>.Success(entity));

        _mockCache
            .Setup(c => c.TryGetAsync<long?>(
                It.Is<string>(s => s == entity.Uid()),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MaybeValue<long?>.FromValue(entity.Generation()));

        var reconciler = CreateReconcilerForController(mockController.Object);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockLogger.Verify(logger => logger.Log(
                    It.Is<LogLevel>(logLevel => logLevel == LogLevel.Debug),
                    It.Is<EventId>(eventId => eventId.Id == 0),
                    It.Is<It.IsAnyType>((@object, type) => @object.ToString() == $"""Entity "{entity.Kind}/{entity.Name()}" modification did not modify generation. Skip event.""" && type.Name == "FormattedLogValues"),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
                Times.Once);
    }

    [Fact]
    public async Task Reconcile_Should_Call_ReconcileAsync_For_Added_Event()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        var mockController = new Mock<IEntityController<V1ConfigMap>>();

        mockController
            .Setup(c => c.ReconcileAsync(It.IsAny<V1ConfigMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1ConfigMap>.Success(entity));

        var reconciler = CreateReconcilerForController(mockController.Object);

        await reconciler.Reconcile(context, CancellationToken.None);

        mockController.Verify(
            c => c.ReconcileAsync(entity, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_Should_Call_ReconcileAsync_For_Modified_Event_With_No_Deletion_Timestamp()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Modified);
        var mockController = new Mock<IEntityController<V1ConfigMap>>();

        mockController
            .Setup(c => c.ReconcileAsync(It.IsAny<V1ConfigMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1ConfigMap>.Success(entity));

        var reconciler = CreateReconcilerForController(mockController.Object);

        await reconciler.Reconcile(context, CancellationToken.None);

        mockController.Verify(
            c => c.ReconcileAsync(entity, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_Should_Call_FinalizeAsync_For_Modified_Event_With_Deletion_Timestamp()
    {
        const string finalizerName = "test-finalizer";
        var entity = CreateTestEntityForFinalization(deletionTimestamp: DateTime.UtcNow, finalizer: finalizerName);
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Modified);

        var mockFinalizer = new Mock<IEntityFinalizer<V1ConfigMap>>();

        mockFinalizer
            .Setup(c => c.FinalizeAsync(It.IsAny<V1ConfigMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1ConfigMap>.Success(entity));

        var reconciler = CreateReconcilerForFinalizer(mockFinalizer.Object, finalizerName);

        await reconciler.Reconcile(context, CancellationToken.None);

        mockFinalizer.Verify(
            c => c.FinalizeAsync(entity, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_Should_Call_DeletedAsync_For_Deleted_Event()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Deleted);
        var mockController = new Mock<IEntityController<V1ConfigMap>>();

        mockController
            .Setup(c => c.DeletedAsync(It.IsAny<V1ConfigMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1ConfigMap>.Success(entity));

        var reconciler = CreateReconcilerForController(mockController.Object);

        await reconciler.Reconcile(context, CancellationToken.None);

        mockController.Verify(
            c => c.DeletedAsync(entity, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_Should_Remove_From_Cache_After_Successful_Deletion()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Deleted);

        var controller = CreateMockController(
            deletedResult: ReconciliationResult<V1ConfigMap>.Success(entity));

        var reconciler = CreateReconcilerForController(controller);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockCache.Verify(
            c => c.RemoveAsync(entity.Uid(), It.IsAny<FusionCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_Should_Not_Remove_From_Cache_After_Failed_Deletion()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Deleted);

        var controller = CreateMockController(
            deletedResult: ReconciliationResult<V1ConfigMap>.Failure(entity, "Deletion failed"));

        var reconciler = CreateReconcilerForController(controller);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockCache.Verify(
            c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<FusionCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(RequeueType.Added)]
    [InlineData(RequeueType.Modified)]
    [InlineData(RequeueType.Deleted)]
    public async Task Reconcile_Should_Use_Correct_RequeueType_For_EventType(RequeueType expectedRequeueType)
    {
        var entity = CreateTestEntity();
        var requeueAfter = TimeSpan.FromSeconds(30);
        var watchEventType = expectedRequeueType switch
        {
            RequeueType.Added => WatchEventType.Added,
            RequeueType.Modified => WatchEventType.Modified,
            RequeueType.Deleted => WatchEventType.Deleted,
            _ => throw new ArgumentException("Invalid RequeueType"),
        };

        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, watchEventType);
        var result = ReconciliationResult<V1ConfigMap>.Success(entity, requeueAfter);

        var controller = watchEventType == WatchEventType.Deleted
            ? CreateMockController(deletedResult: result)
            : CreateMockController(reconcileResult: result);

        var reconciler = CreateReconcilerForController(controller);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockQueue.Verify(
            q => q.Enqueue(
                entity,
                expectedRequeueType,
                requeueAfter,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_Should_Return_Result_From_Controller()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        var expectedResult = ReconciliationResult<V1ConfigMap>.Success(entity, TimeSpan.FromMinutes(1));

        var controller = CreateMockController(reconcileResult: expectedResult);
        var reconciler = CreateReconcilerForController(controller);

        var result = await reconciler.Reconcile(context, CancellationToken.None);

        result.Should().Be(expectedResult);
        result.Entity.Should().Be(entity);
        result.RequeueAfter.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Reconcile_Should_Handle_Failure_Result()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        const string errorMessage = "Test error";
        var failureResult = ReconciliationResult<V1ConfigMap>.Failure(entity, errorMessage);

        var controller = CreateMockController(reconcileResult: failureResult);
        var reconciler = CreateReconcilerForController(controller);

        var result = await reconciler.Reconcile(context, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be(errorMessage);
    }

    [Fact]
    public async Task Reconcile_Should_Enqueue_Failed_Result_If_RequeueAfter_Set()
    {
        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Added);
        var requeueAfter = TimeSpan.FromSeconds(30);
        var failureResult = ReconciliationResult<V1ConfigMap>.Failure(
            entity,
            "Temporary failure",
            requeueAfter: requeueAfter);

        var controller = CreateMockController(reconcileResult: failureResult);
        var reconciler = CreateReconcilerForController(controller);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockQueue.Verify(
            q => q.Enqueue(
                entity,
                RequeueType.Added,
                requeueAfter,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_When_Auto_Attach_Finalizers_Is_Enabled_Should_Attach_Finalizer()
    {
        _settings.AutoAttachFinalizers = true;

        var entity = CreateTestEntity();
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Modified);
        var mockController = new Mock<IEntityController<V1ConfigMap>>();
        var mockFinalizer = new Mock<IEntityFinalizer<V1ConfigMap>>();

        _mockClient
            .Setup(c => c.UpdateAsync(It.Is<V1ConfigMap>(
                e => e == entity),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockServiceProvider
            .Setup(p => p.GetRequiredKeyedService(
                It.Is<Type>(t => t == typeof(IEnumerable<IEntityFinalizer<V1ConfigMap>>)),
                It.Is<object?>(o => ReferenceEquals(o, KeyedService.AnyKey))))
            .Returns(new List<IEntityFinalizer<V1ConfigMap>> { mockFinalizer.Object });

        mockController
            .Setup(c => c.ReconcileAsync(It.IsAny<V1ConfigMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1ConfigMap>.Success(entity));

        var reconciler = CreateReconcilerForController(mockController.Object);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockClient.Verify(
            c => c.UpdateAsync(It.Is<V1ConfigMap>(cm => cm.HasFinalizer("ientityfinalizer`1proxyfinalizer")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reconcile_When_Auto_Detach_Finalizers_Is_Enabled_Should_Detach_Finalizer()
    {
        _settings.AutoDetachFinalizers = true;

        const string finalizerName = "test-finalizer";
        var entity = CreateTestEntityForFinalization(deletionTimestamp: DateTime.UtcNow, finalizer: finalizerName);
        var context = ReconciliationContext<V1ConfigMap>.CreateFromApiServerEvent(entity, WatchEventType.Modified);

        var mockFinalizer = new Mock<IEntityFinalizer<V1ConfigMap>>();

        _mockClient
            .Setup(c => c.UpdateAsync(It.Is<V1ConfigMap>(
                    e => e == entity),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        mockFinalizer
            .Setup(c => c.FinalizeAsync(It.IsAny<V1ConfigMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReconciliationResult<V1ConfigMap>.Success(entity));

        var reconciler = CreateReconcilerForFinalizer(mockFinalizer.Object, finalizerName);

        await reconciler.Reconcile(context, CancellationToken.None);

        _mockClient.Verify(
            c => c.UpdateAsync(It.Is<V1ConfigMap>(cm => !cm.HasFinalizer(finalizerName)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private Reconciler<V1ConfigMap> CreateReconcilerForController(IEntityController<V1ConfigMap> controller)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();

        mockScope
            .Setup(s => s.ServiceProvider)
            .Returns(_mockServiceProvider.Object);

        mockScopeFactory
            .Setup(s => s.CreateScope())
            .Returns(mockScope.Object);

        _mockServiceProvider
            .Setup(p => p.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        _mockServiceProvider
            .Setup(p => p.GetService(typeof(IEntityController<V1ConfigMap>)))
            .Returns(controller);

        return new(
            _mockLogger.Object,
            _mockCacheProvider.Object,
            _mockServiceProvider.Object,
            _settings,
            _mockQueue.Object,
            _mockClient.Object);
    }

    private Reconciler<V1ConfigMap> CreateReconcilerForFinalizer(IEntityFinalizer<V1ConfigMap>? finalizer, string finalizerName)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();

        mockScope
            .Setup(s => s.ServiceProvider)
            .Returns(_mockServiceProvider.Object);

        mockScopeFactory
            .Setup(s => s.CreateScope())
            .Returns(mockScope.Object);

        _mockServiceProvider
            .Setup(p => p.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        _mockServiceProvider
            .Setup(p => p.GetKeyedService(
                It.Is<Type>(t => t == typeof(IEntityFinalizer<V1ConfigMap>)),
                It.Is<string>(s => s == finalizerName)))
            .Returns(finalizer);

        return new(
            _mockLogger.Object,
            _mockCacheProvider.Object,
            _mockServiceProvider.Object,
            _settings,
            _mockQueue.Object,
            _mockClient.Object);
    }

    private static IEntityController<V1ConfigMap> CreateMockController(
        ReconciliationResult<V1ConfigMap>? reconcileResult = null,
        ReconciliationResult<V1ConfigMap>? deletedResult = null)
    {
        var mockController = new Mock<IEntityController<V1ConfigMap>>();
        var entity = CreateTestEntity();

        mockController
            .Setup(c => c.ReconcileAsync(It.IsAny<V1ConfigMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reconcileResult ?? ReconciliationResult<V1ConfigMap>.Success(entity));

        mockController
            .Setup(c => c.DeletedAsync(It.IsAny<V1ConfigMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedResult ?? ReconciliationResult<V1ConfigMap>.Success(entity));

        return mockController.Object;
    }

    private static V1ConfigMap CreateTestEntity(string? name = null)
    {
        return new()
        {
            Metadata = new()
            {
                Name = name ?? "test-configmap",
                NamespaceProperty = "default",
                Uid = Guid.NewGuid().ToString(),
                Generation = 1,
            },
            Kind = V1ConfigMap.KubeKind,
        };
    }

    private static V1ConfigMap CreateTestEntityForFinalization(string? name = null, DateTime? deletionTimestamp = null, string? finalizer = null)
    {
        return new()
        {
            Metadata = new()
            {
                Name = name ?? "test-configmap",
                NamespaceProperty = "default",
                Uid = Guid.NewGuid().ToString(),
                Generation = 1,
                DeletionTimestamp = deletionTimestamp,
                Finalizers = !string.IsNullOrEmpty(finalizer) ? new List<string> { finalizer } : new(),
            },
            Kind = V1ConfigMap.KubeKind,
        };
    }
}
