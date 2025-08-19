// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;

using k8s;
using k8s.Autorest;
using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Transpiler;

namespace KubeOps.KubernetesClient;

/// <inheritdoc cref="IKubernetesClient"/>
public class KubernetesClient : IKubernetesClient
{
    private const string DownwardApiNamespaceFile = "/var/run/secrets/kubernetes.io/serviceaccount/namespace";
    private const string DefaultNamespace = "default";

    private static readonly ConcurrentDictionary<Type, EntityMetadata> MetadataCache = new();

    private readonly KubernetesClientConfiguration _clientConfig;
    private bool _disposed;

    /// <summary>
    /// Create a new Kubernetes client for the given entity.
    /// The client will use the default configuration.
    /// </summary>
    public KubernetesClient()
        : this(KubernetesClientConfiguration.BuildDefaultConfig())
    {
    }

    /// <summary>
    /// Create a new Kubernetes client for the given entity with a custom client configuration.
    /// </summary>
    /// <param name="clientConfig">The config for the underlying Kubernetes client.</param>
    public KubernetesClient(KubernetesClientConfiguration clientConfig)
        : this(clientConfig, new Kubernetes(clientConfig))
    {
    }

    /// <summary>
    /// Create a new Kubernetes client for the given entity with a custom client configuration and client.
    /// </summary>
    /// <remarks>
    /// <paramref name="client"/> is automatically disposed if this <see cref="KubernetesClient"/> is also being disposed.
    /// </remarks>
    /// <param name="clientConfig">The config for the underlying Kubernetes client.</param>
    /// <param name="client">The underlying client.</param>
    public KubernetesClient(KubernetesClientConfiguration clientConfig, IKubernetes client)
    {
        _clientConfig = clientConfig;
        ApiClient = client;
    }

    /// <inheritdoc />
    public IKubernetes ApiClient { get; }

    /// <inheritdoc />
    public Uri BaseUri => ApiClient.BaseUri;

    /// <summary>
    /// Clears the metadata cache.
    /// </summary>
    public static void ClearMetadataCache() => MetadataCache.Clear();

    /// <inheritdoc />
    public async Task<string> GetCurrentNamespaceAsync(
        string downwardApiEnvName = "POD_NAMESPACE",
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_clientConfig.Namespace is { } configValue)
        {
            return configValue;
        }

        if (Environment.GetEnvironmentVariable(downwardApiEnvName) is { } envValue)
        {
            return envValue;
        }

        if (File.Exists(DownwardApiNamespaceFile))
        {
            var ns = await File.ReadAllTextAsync(DownwardApiNamespaceFile, cancellationToken);
            return ns.Trim();
        }

        return DefaultNamespace;
    }

    /// <inheritdoc />
    public string GetCurrentNamespace(string downwardApiEnvName = "POD_NAMESPACE")
    {
        ThrowIfDisposed();

        if (_clientConfig.Namespace is { } configValue)
        {
            return configValue;
        }

        if (Environment.GetEnvironmentVariable(downwardApiEnvName) is { } envValue)
        {
            return envValue;
        }

        if (File.Exists(DownwardApiNamespaceFile))
        {
            var ns = File.ReadAllText(DownwardApiNamespaceFile);
            return ns.Trim();
        }

        return DefaultNamespace;
    }

    /// <inheritdoc />
    public async Task<TEntity?> GetAsync<TEntity>(
        string name,
        string? @namespace = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        var metadata = GetMetadata<TEntity>();

        try
        {
            return await (string.IsNullOrWhiteSpace(@namespace)
                ? ApiClient.CustomObjects.GetClusterCustomObjectAsync<TEntity>(
                    metadata.Group ?? string.Empty,
                    metadata.Version,
                    metadata.PluralName,
                    name,
                    cancellationToken: cancellationToken)
                : ApiClient.CustomObjects.GetNamespacedCustomObjectAsync<TEntity>(
                    metadata.Group ?? string.Empty,
                    metadata.Version,
                    @namespace,
                    metadata.PluralName,
                    name,
                    cancellationToken: cancellationToken));
        }
        catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    /// <inheritdoc />
    public TEntity? Get<TEntity>(string name, string? @namespace = null)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        var metadata = GetMetadata<TEntity>();

        try
        {
            return string.IsNullOrWhiteSpace(@namespace)
                ? ApiClient.CustomObjects.GetClusterCustomObject<TEntity>(
                    metadata.Group ?? string.Empty,
                    metadata.Version,
                    metadata.PluralName,
                    name)
                : ApiClient.CustomObjects.GetNamespacedCustomObject<TEntity>(
                    metadata.Group ?? string.Empty,
                    metadata.Version,
                    @namespace,
                    metadata.PluralName,
                    name);
        }
        catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    /// <inheritdoc />
    public async Task<IList<TEntity>> ListAsync<TEntity>(
        string? @namespace = null,
        string? labelSelector = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        var metadata = GetMetadata<TEntity>();
        return (@namespace switch
        {
            null => await ApiClient.CustomObjects.ListClusterCustomObjectAsync<EntityList<TEntity>>(
                metadata.Group ?? string.Empty,
                metadata.Version,
                metadata.PluralName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken),
            _ => await ApiClient.CustomObjects.ListNamespacedCustomObjectAsync<EntityList<TEntity>>(
                metadata.Group ?? string.Empty,
                metadata.Version,
                @namespace,
                metadata.PluralName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken),
        }).Items;
    }

    /// <inheritdoc />
    public IList<TEntity> List<TEntity>(string? @namespace = null, string? labelSelector = null)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        var metadata = GetMetadata<TEntity>();
        return (@namespace switch
        {
            null => ApiClient.CustomObjects.ListClusterCustomObject<EntityList<TEntity>>(
                metadata.Group ?? string.Empty,
                metadata.Version,
                metadata.PluralName,
                labelSelector: labelSelector),
            _ => ApiClient.CustomObjects.ListNamespacedCustomObject<EntityList<TEntity>>(
                metadata.Group ?? string.Empty,
                metadata.Version,
                @namespace,
                metadata.PluralName,
                labelSelector: labelSelector),
        }).Items;
    }

    /// <inheritdoc />
    public async Task<TEntity> CreateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        using var client = CreateGenericClient<TEntity>();
        return await (entity.Namespace() switch
        {
            { } ns => client.CreateNamespacedAsync(entity, ns, cancellationToken),
            null => client.CreateAsync(entity, cancellationToken),
        });
    }

    /// <inheritdoc />
    public async Task<TEntity> UpdateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        using var client = CreateGenericClient<TEntity>();
        return await (entity.Namespace() switch
        {
            { } ns => client.ReplaceNamespacedAsync(entity, ns, entity.Name(), cancellationToken),
            null => client.ReplaceAsync(entity, entity.Name(), cancellationToken),
        });
    }

    /// <inheritdoc />
    public Task<TEntity> UpdateStatusAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        var metadata = GetMetadata<TEntity>();
        return entity.Namespace() switch
        {
            { } ns => ApiClient.CustomObjects.ReplaceNamespacedCustomObjectStatusAsync<TEntity>(
                entity,
                metadata.Group ?? string.Empty,
                metadata.Version,
                ns,
                metadata.PluralName,
                entity.Name(),
                cancellationToken: cancellationToken),
            _ => ApiClient.CustomObjects.ReplaceClusterCustomObjectStatusAsync<TEntity>(
                entity,
                metadata.Group ?? string.Empty,
                metadata.Version,
                metadata.PluralName,
                entity.Name(),
                cancellationToken: cancellationToken),
        };
    }

    /// <inheritdoc />
    public TEntity UpdateStatus<TEntity>(TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        var metadata = GetMetadata<TEntity>();
        return entity.Namespace() switch
        {
            { } ns => ApiClient.CustomObjects.ReplaceNamespacedCustomObjectStatus<TEntity>(
                entity,
                metadata.Group ?? string.Empty,
                metadata.Version,
                ns,
                metadata.PluralName,
                entity.Name()),
            _ => ApiClient.CustomObjects.ReplaceClusterCustomObjectStatus<TEntity>(
                entity,
                metadata.Group ?? string.Empty,
                metadata.Version,
                metadata.PluralName,
                entity.Name()),
        };
    }

    /// <inheritdoc />
    public async Task<TEntity> PatchAsync<TEntity>(
        V1Patch patch,
        string name,
        string? @namespace = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        using var client = CreateGenericClient<TEntity>();
        return await (@namespace switch
        {
            not null => client.PatchNamespacedAsync<TEntity>(patch, @namespace, name, cancellationToken),
            null => client.PatchAsync<TEntity>(patch, name, cancellationToken),
        });
    }

    /// <inheritdoc />
    public async Task DeleteAsync<TEntity>(
        string name,
        string? @namespace = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();

        try
        {
            using var client = CreateGenericClient<TEntity>();
            switch (@namespace)
            {
                case not null:
                    await client.DeleteNamespacedAsync<V1Status>(@namespace, name, cancellationToken);
                    break;

                default:
                    await client.DeleteAsync<V1Status>(name, cancellationToken);
                    break;
            }
        }
        catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
        {
            // The resource was not found. We can ignore this.
        }
    }

    /// <inheritdoc />
    public Watcher<TEntity> Watch<TEntity>(
        Action<WatchEventType, TEntity> onEvent,
        Action<Exception>? onError = null,
        Action? onClose = null,
        string? @namespace = null,
        TimeSpan? timeout = null,
        bool? allowWatchBookmarks = null,
        string? resourceVersion = null,
        string? labelSelector = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();
        var metadata = GetMetadata<TEntity>();
        return (@namespace switch
        {
            not null => ApiClient.CustomObjects.ListNamespacedCustomObjectWithHttpMessagesAsync(
                metadata.Group ?? string.Empty,
                metadata.Version,
                @namespace,
                metadata.PluralName,
                allowWatchBookmarks: allowWatchBookmarks,
                labelSelector: labelSelector,
                resourceVersion: resourceVersion,
                timeoutSeconds: timeout switch
                {
                    null => null,
                    _ => (int?)timeout.Value.TotalSeconds,
                },
                watch: true,
                cancellationToken: cancellationToken),
            _ => ApiClient.CustomObjects.ListClusterCustomObjectWithHttpMessagesAsync(
                metadata.Group ?? string.Empty,
                metadata.Version,
                metadata.PluralName,
                allowWatchBookmarks: allowWatchBookmarks,
                labelSelector: labelSelector,
                resourceVersion: resourceVersion,
                timeoutSeconds: timeout switch
                {
                    null => null,
                    _ => (int?)timeout.Value.TotalSeconds,
                },
                watch: true,
                cancellationToken: cancellationToken),
        }).Watch(onEvent, onError, onClose);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<(WatchEventType Type, TEntity Entity)> WatchAsync<TEntity>(
        string? @namespace = null,
        string? resourceVersion = null,
        string? labelSelector = null,
        bool? allowWatchBookmarks = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        ThrowIfDisposed();
        var metadata = GetMetadata<TEntity>();
        var watcher = (@namespace switch
        {
            not null => ApiClient.CustomObjects.ListNamespacedCustomObjectWithHttpMessagesAsync(
                metadata.Group ?? string.Empty,
                metadata.Version,
                @namespace,
                metadata.PluralName,
                allowWatchBookmarks: allowWatchBookmarks,
                labelSelector: labelSelector,
                resourceVersion: resourceVersion,
                watch: true,
                cancellationToken: cancellationToken),
            _ => ApiClient.CustomObjects.ListClusterCustomObjectWithHttpMessagesAsync(
                metadata.Group ?? string.Empty,
                metadata.Version,
                metadata.PluralName,
                allowWatchBookmarks: allowWatchBookmarks,
                labelSelector: labelSelector,
                resourceVersion: resourceVersion,
                watch: true,
                cancellationToken: cancellationToken),
        }).WatchAsync<TEntity, object>(
            onError: ex => throw ex,
            cancellationToken: cancellationToken);

        await foreach ((WatchEventType watchEventType, TEntity? entity) in watcher)
        {
            if (entity is null)
            {
                continue;
            }

            yield return (watchEventType, entity);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        // The property is intentionally set before the underlying _client is disposed.
        // This ensures that even if the disposal of the client is not finished yet, that all calls to the client
        // are instantly failing.
        _disposed = true;
        ApiClient.Dispose();
    }

    private static EntityMetadata GetMetadata<TEntity>()
    {
        var type = typeof(TEntity);
        return MetadataCache.GetOrAdd(type, t => Entities.ToEntityMetadata(t).Metadata);
    }

    [DebuggerHidden]
    private void ThrowIfDisposed()
    {
        if (!_disposed)
        {
            return;
        }

        throw new ObjectDisposedException(nameof(KubernetesClient));
    }

    private GenericClient CreateGenericClient<TEntity>()
    {
        ThrowIfDisposed();
        var metadata = GetMetadata<TEntity>();
        return metadata.Group switch
        {
            null => new GenericClient(
                ApiClient,
                metadata.Version,
                metadata.PluralName,
                false),
            _ => new GenericClient(
                ApiClient,
                metadata.Group,
                metadata.Version,
                metadata.PluralName,
                false),
        };
    }
}
