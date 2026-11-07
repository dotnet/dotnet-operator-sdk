// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.KubernetesClient;

namespace KubeOps.Operator.Web.Test.Webhooks;

internal sealed class CapturingKubernetesClient : IKubernetesClient
{
    /// <summary>
    /// Gets the objects returned by <see cref="GetAsync{TEntity}"/>, keyed by name.
    /// </summary>
    public Dictionary<string, IKubernetesObject<V1ObjectMeta>> Existing { get; } = [];

    /// <summary>
    /// Gets the objects passed to <see cref="UpdateAsync{TEntity}"/>.
    /// </summary>
    public List<IKubernetesObject<V1ObjectMeta>> Updated { get; } = [];

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
        => Task.FromResult(Existing.TryGetValue(name, out var existing) && existing is TEntity entity
            ? entity
            : default);

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
    {
        Updated.Add(entity);
        return Task.FromResult(entity);
    }

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
