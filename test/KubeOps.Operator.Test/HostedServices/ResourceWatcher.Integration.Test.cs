// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Controller;
using KubeOps.Operator.Test.TestEntities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KubeOps.Operator.Test.HostedServices;

public class HostedServiceDisposeIntegrationTest : IntegrationTestBase
{
    [Fact]
    public async Task Should_Allow_DisposeAsync_Before_StopAsync()
    {
        var hostedServices = Services.GetServices<IHostedService>()
            .Where(service => service.GetType().Namespace!.StartsWith("KubeOps"));

        // We need to test the inverse order, because the Host is usually disposing the resources in advance of
        // stopping them.
        foreach (IHostedService service in hostedServices)
        {
            await Assert.IsAssignableFrom<IAsyncDisposable>(service).DisposeAsync();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Should_Allow_StopAsync_Before_DisposeAsync()
    {
        var hostedServices = Services.GetServices<IHostedService>()
            .Where(service => service.GetType().Namespace!.StartsWith("KubeOps"));

        foreach (IHostedService service in hostedServices)
        {
            await service.StopAsync(CancellationToken.None);
            await Assert.IsAssignableFrom<IAsyncDisposable>(service).DisposeAsync();
        }
    }

    protected override void ConfigureHost(HostApplicationBuilder builder)
    {
        builder.Services
            .AddKubernetesOperator()
            .AddController<TestController, V1OperatorIntegrationTestEntity>();
    }

    private class TestController : IEntityController<V1OperatorIntegrationTestEntity>
    {
        public Task<Result<V1OperatorIntegrationTestEntity>> ReconcileAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<V1OperatorIntegrationTestEntity>.ForSuccess(entity));

        public Task<Result<V1OperatorIntegrationTestEntity>> DeletedAsync(V1OperatorIntegrationTestEntity entity, CancellationToken cancellationToken)
            => Task.FromResult(Result<V1OperatorIntegrationTestEntity>.ForSuccess(entity));
    }
}
