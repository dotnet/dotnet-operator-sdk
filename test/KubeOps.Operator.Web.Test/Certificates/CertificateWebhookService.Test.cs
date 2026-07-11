// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Webhooks;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Web.Certificates;
using KubeOps.Operator.Web.Test.TestApp;

using Microsoft.Extensions.Logging.Abstractions;

namespace KubeOps.Operator.Web.Test.Certificates;

[Trait("Area", "Certificate")]
public sealed class CertificateWebhookServiceTest : IDisposable
{
    private readonly CertificateGenerator _certificateProvider = new(Environment.MachineName);

    [Fact]
    public async Task StartAsync_Uses_Root_Certificate_As_Webhook_CaBundle()
    {
        var client = new CapturingKubernetesClient();
        var factory = new CapturingWebhookConfigurationFactory();
        var service = new CertificateWebhookService(
            NullLogger<CertificateWebhookService>.Instance,
            client,
            new(typeof(V1OperatorWebIntegrationTestEntity).Assembly),
            new(Environment.MachineName, 443),
            _certificateProvider,
            factory);

        await service.StartAsync(TestContext.Current.CancellationToken);

        var expectedRootCaBundle = _certificateProvider.Root.Certificate.EncodeToPemBytes();
        var serverCertificateBundle = _certificateProvider.Server.Certificate.EncodeToPemBytes();

        factory.ValidatingCaBundles.Should()
            .NotBeEmpty()
            .And.OnlyContain(caBundle => caBundle.SequenceEqual(expectedRootCaBundle));
        factory.MutatingCaBundles.Should()
            .NotBeEmpty()
            .And.OnlyContain(caBundle => caBundle.SequenceEqual(expectedRootCaBundle));

        factory.ValidatingCaBundles.Should()
            .NotContain(caBundle => caBundle.SequenceEqual(serverCertificateBundle));
        factory.MutatingCaBundles.Should()
            .NotContain(caBundle => caBundle.SequenceEqual(serverCertificateBundle));
    }

    public void Dispose()
    {
        _certificateProvider.Dispose();
    }

    private sealed class CapturingWebhookConfigurationFactory : IWebhookConfigurationFactory
    {
        public List<byte[]> MutatingCaBundles { get; } = [];

        public List<byte[]> ValidatingCaBundles { get; } = [];

        public V1MutatingWebhookConfiguration CreateMutatingConfiguration(
            IEnumerable<MutatingWebhookRegistration> registrations)
        {
            MutatingCaBundles.AddRange(registrations.Select(r => r.CaBundle).OfType<byte[]>());

            return new()
            {
                Metadata = new() { Name = "mutators" },
                Webhooks = [new() { Name = "mutate.test.kubeops.dev.v1" }],
            };
        }

        public V1ValidatingWebhookConfiguration CreateValidatingConfiguration(
            IEnumerable<ValidatingWebhookRegistration> registrations)
        {
            ValidatingCaBundles.AddRange(registrations.Select(r => r.CaBundle).OfType<byte[]>());

            return new()
            {
                Metadata = new() { Name = "validators" },
                Webhooks = [new() { Name = "validate.test.kubeops.dev.v1" }],
            };
        }
    }

    private sealed class CapturingKubernetesClient : IKubernetesClient
    {
        public IKubernetes ApiClient => throw new NotSupportedException();

        public Uri BaseUri => new("https://localhost");

        public void Dispose()
        {
        }

        public Task<string> GetCurrentNamespaceAsync(
            string downwardApiEnvName = "POD_NAMESPACE",
            CancellationToken cancellationToken = default)
            => Task.FromResult("default");

        public string GetCurrentNamespace(string downwardApiEnvName = "POD_NAMESPACE") => "default";

        public Task<TEntity?> GetAsync<TEntity>(
            string name,
            string? @namespace = null,
            CancellationToken cancellationToken = default)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => Task.FromResult<TEntity?>(default);

        public TEntity? Get<TEntity>(string name, string? @namespace = null)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => default;

        public Task<IList<TEntity>> ListAsync<TEntity>(
            string? @namespace = null,
            string? labelSelector = null,
            string? fieldSelector = null,
            CancellationToken cancellationToken = default)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => Task.FromResult<IList<TEntity>>([]);

        public IList<TEntity> List<TEntity>(
            string? @namespace = null,
            string? labelSelector = null,
            string? fieldSelector = null)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => [];

        public Task<TEntity> CreateAsync<TEntity>(
            TEntity entity,
            CancellationToken cancellationToken = default)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => Task.FromResult(entity);

        public Task<TEntity> UpdateAsync<TEntity>(
            TEntity entity,
            CancellationToken cancellationToken = default)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => Task.FromResult(entity);

        public Task<TEntity> UpdateStatusAsync<TEntity>(
            TEntity entity,
            CancellationToken cancellationToken = default)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => Task.FromResult(entity);

        public TEntity UpdateStatus<TEntity>(TEntity entity)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => entity;

        public Task<TEntity> PatchAsync<TEntity>(
            V1Patch patch,
            string name,
            string? @namespace = null,
            string? fieldManager = null,
            bool? force = null,
            string? dryRun = null,
            CancellationToken cancellationToken = default)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => throw new NotSupportedException();

        public Task DeleteAsync<TEntity>(
            string name,
            string? @namespace = null,
            CancellationToken cancellationToken = default)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => Task.CompletedTask;

        public Watcher<TEntity> Watch<TEntity>(
            Action<WatchEventType, TEntity> onEvent,
            Action<Exception>? onError = null,
            Action? onClose = null,
            string? @namespace = null,
            TimeSpan? timeout = null,
            bool? allowWatchBookmarks = null,
            string? resourceVersion = null,
            string? labelSelector = null,
            string? fieldSelector = null,
            CancellationToken cancellationToken = default)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => throw new NotSupportedException();

        public IAsyncEnumerable<(WatchEventType Type, TEntity Entity)> WatchAsync<TEntity>(
            string? @namespace = null,
            string? resourceVersion = null,
            string? labelSelector = null,
            string? fieldSelector = null,
            bool? allowWatchBookmarks = null,
            CancellationToken cancellationToken = default)
            where TEntity : IKubernetesObject<V1ObjectMeta>
            => throw new NotSupportedException();
    }
}
