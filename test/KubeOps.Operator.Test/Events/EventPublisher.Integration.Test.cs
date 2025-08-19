// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Events;
using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.Events;

public class EventPublisherIntegrationTest : IntegrationTestBase
{
    private readonly InvocationCounter<V1OperatorIntegrationTestEntity> _mock = new();
    private readonly IKubernetesClient _client = new KubernetesClient.KubernetesClient();
    private readonly TestNamespaceProvider _ns = new();

    [Fact]
    public async Task Should_Create_New_Event()
    {
        var entity =
            await _client.CreateAsync(new V1OperatorIntegrationTestEntity("single-entity", "username", _ns.Namespace));
        await _mock.WaitForInvocations;

        var eventName = $"{entity.Uid()}.single-entity.{_ns.Namespace}.REASON.message.Normal";
        var encodedEventName =
            Convert.ToHexString(
                SHA512.HashData(
                    Encoding.UTF8.GetBytes(eventName)));

        var e = await _client.GetAsync<Corev1Event>(encodedEventName, _ns.Namespace);
        e!.Count.Should().Be(1);
        e.Metadata.Annotations.Should().Contain(a => a.Key == "originalName" && a.Value == eventName);
    }

    [Fact]
    public async Task Should_Increase_Count_On_Existing_Event()
    {
        _mock.TargetInvocationCount = 5;
        var entity =
            await _client.CreateAsync(new V1OperatorIntegrationTestEntity("test-entity", "username", _ns.Namespace));
        await _mock.WaitForInvocations;

        var eventName = $"{entity.Uid()}.test-entity.{_ns.Namespace}.REASON.message.Normal";
        var encodedEventName =
            Convert.ToHexString(
                SHA512.HashData(
                    Encoding.UTF8.GetBytes(eventName)));

        var e = await _client.GetAsync<Corev1Event>(encodedEventName, _ns.Namespace);
        e!.Count.Should().Be(5);
        e.Metadata.Annotations.Should().Contain(a => a.Key == "originalName" && a.Value == eventName);
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await _ns.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _ns.DisposeAsync();
        _client.Dispose();
    }

    protected override void ConfigureHost(HostApplicationBuilder builder)
    {
        builder.Services
            .AddSingleton(_mock)
            .AddKubernetesOperator(s => s.Namespace = _ns.Namespace)
            .AddController<TestController, V1OperatorIntegrationTestEntity>();
    }

    private class TestController(
        InvocationCounter<V1OperatorIntegrationTestEntity> svc,
        EntityRequeue<V1OperatorIntegrationTestEntity> requeue,
        EventPublisher eventPublisher)
        : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public async Task<Result<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
        {
            await eventPublisher(entity, "REASON", "message", cancellationToken: cancellationToken);
            svc.Invocation(entity);

            if (svc.Invocations.Count < svc.TargetInvocationCount)
            {
                requeue(entity, TimeSpan.FromMilliseconds(10));
            }

            return Result<V1OperatorIntegrationTestEntity>.ForSuccess(entity);
        }

        public Task<Result<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<V1OperatorIntegrationTestEntity>.ForSuccess(entity));
    }
}
