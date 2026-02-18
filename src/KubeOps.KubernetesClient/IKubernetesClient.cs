// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.Versioning;

using Json.Patch;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.KubernetesClient.LabelSelectors;

namespace KubeOps.KubernetesClient;

/// <summary>
/// Client for the Kubernetes API. Contains various methods to manage Kubernetes entities.
/// This client is generic and allows usage of all types as long as they are decorated
/// with the <see cref="KubernetesEntityAttribute"/>.
/// </summary>
public interface IKubernetesClient : IDisposable
{
    /// <summary>
    /// Represents the "original" kubernetes client from the
    /// "KubernetesClient" package.
    /// </summary>
    IKubernetes ApiClient { get; }

    /// <summary>
    /// Return the base URI of the currently used KubernetesClient.
    /// </summary>
    Uri BaseUri { get; }

    /// <summary>
    /// Returns the name of the current namespace.
    /// To determine the current namespace the following places (in the given order) are checked:
    /// <list type="number">
    /// <item>
    /// <description>The created Kubernetes configuration (from file / in-cluster)</description>
    /// </item>
    /// <item>
    /// <description>
    ///     The env variable given as the param to the function (default "POD_NAMESPACE")
    ///     which can be provided by the <a href="https://Kubernetes.io/docs/tasks/inject-data-application/downward-API-volume-expose-pod-information/#capabilities-of-the-downward-API">Kubernetes downward API</a>
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    ///     The fallback secret file if running on the cluster
    ///     (<c>/var/run/secrets/Kubernetes.io/serviceaccount/namespace</c>)
    /// </description>
    /// </item>
    /// <item>
    /// <description>`default`</description>
    /// </item>
    /// </list>
    /// </summary>
    /// <param name="downwardApiEnvName">Customizable name of the env var to check for the namespace.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A string containing the current namespace (or a fallback of it).</returns>
    Task<string> GetCurrentNamespaceAsync(
        string downwardApiEnvName = "POD_NAMESPACE",
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="GetCurrentNamespaceAsync" />
    string GetCurrentNamespace(string downwardApiEnvName = "POD_NAMESPACE");

    /// <summary>
    /// Fetch and return an entity from the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="name">The name of the entity (metadata.name).</param>
    /// <param name="namespace">
    /// Optional namespace. If this is set, the entity must be a namespaced entity.
    /// If it is omitted, the entity must be a cluster wide entity.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The found entity of the given type, or null otherwise.</returns>
    Task<TEntity?> GetAsync<TEntity>(
        string name,
        string? @namespace = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <inheritdoc cref="GetAsync{TEntity}"/>
    TEntity? Get<TEntity>(string name, string? @namespace = null)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <summary>
    /// Fetch and return a list of entities from the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="namespace">If the entities are namespaced, provide the name of the namespace.</param>
    /// <param name="labelSelector">A string, representing an optional label selector for filtering fetched objects.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A list of Kubernetes entities.</returns>
    Task<IList<TEntity>> ListAsync<TEntity>(
        string? @namespace = null,
        string? labelSelector = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <summary>
    /// Fetch and return a list of entities from the Kubernetes API.
    /// </summary>
    /// <remarks>
    /// This is invoking the API without any cancellation support. In order to pass a <see cref="CancellationToken"/>,
    /// you need to use the <see cref="CreateAsync{TEntity}(IEnumerable{TEntity},CancellationToken)"/> overload.
    /// </remarks>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="namespace">
    /// If only entities in a given namespace should be listed, provide the namespace here.
    /// </param>
    /// <param name="labelSelectors">A list of label-selectors to apply to the search.</param>
    /// <returns>A list of Kubernetes entities.</returns>
    Task<IList<TEntity>> ListAsync<TEntity>(
        string? @namespace = null,
        params LabelSelector[] labelSelectors)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        using var cts = new CancellationTokenSource();
        return ListAsync<TEntity>(@namespace, labelSelectors.ToExpression(), cts.Token);
    }

    /// <inheritdoc cref="ListAsync{TEntity}(string?,string?,CancellationToken)"/>
    IList<TEntity> List<TEntity>(
        string? @namespace = null,
        string? labelSelector = null)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <inheritdoc cref="ListAsync{TEntity}(string?,LabelSelector[])"/>
    IList<TEntity> List<TEntity>(
        string? @namespace = null,
        params LabelSelector[] labelSelectors)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => List<TEntity>(@namespace, labelSelectors.ToExpression());

    /// <summary>
    /// Create or Update an entity. This first fetches the entity from the Kubernetes API
    /// and if it does exist, updates the entity. Otherwise, the entity is created.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity in question.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The saved instance of the entity.</returns>
    async Task<TEntity> SaveAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        =>
            await GetAsync<TEntity>(entity.Name(), entity.Namespace(), cancellationToken) switch
            {
                { } e => await UpdateAsync(entity.WithResourceVersion(e), cancellationToken),
                _ => await CreateAsync(entity, cancellationToken),
            };

    /// <summary>
    /// Create or Update a list of entities. This first fetches each entity from the Kubernetes API
    /// and if it does exist, updates the entity. Otherwise, the entity is created.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entities">The entity list.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The saved instances of the entities.</returns>
    async Task<IEnumerable<TEntity>> SaveAsync<TEntity>(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta> =>
        await Task.WhenAll(entities.Select(entity => SaveAsync(entity, cancellationToken)));

    /// <summary>
    /// Create or Update a list of entities. This first fetches each entity from the Kubernetes API
    /// and if it does exist, updates the entity. Otherwise, the entity is created.
    /// </summary>
    /// <remarks>
    /// This is invoking the API without any cancellation support. In order to pass a <see cref="CancellationToken"/>,
    /// you need to use the <see cref="SaveAsync{TEntity}(IEnumerable{TEntity},CancellationToken)"/> overload.
    /// </remarks>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entities">The entity list.</param>
    /// <returns>The saved instances of the entities.</returns>
    Task<IEnumerable<TEntity>> SaveAsync<TEntity>(params TEntity[] entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        using var cts = new CancellationTokenSource();
        return SaveAsync(entities, cts.Token);
    }

    /// <inheritdoc cref="SaveAsync{TEntity}(TEntity,CancellationToken)"/>
    TEntity Save<TEntity>(TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        =>
            Get<TEntity>(entity.Name(), entity.Namespace()) switch
            {
                { } e => Update(entity.WithResourceVersion(e)),
                _ => Create(entity),
            };

    /// <inheritdoc cref="SaveAsync{TEntity}(IEnumerable{TEntity},CancellationToken)"/>
    IEnumerable<TEntity> Save<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        =>
            entities.Select(Save);

    /// <inheritdoc cref="SaveAsync{TEntity}(IEnumerable{TEntity},CancellationToken)"/>
    IEnumerable<TEntity> Save<TEntity>(params TEntity[] entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        =>
            entities.Select(Save);

    /// <summary>
    /// Create the given entity on the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity instance.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The created instance of the entity.</returns>
    Task<TEntity> CreateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <summary>
    /// Create a list of entities on the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entities">The entity list.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The created instances of the entities.</returns>
    async Task<IEnumerable<TEntity>> CreateAsync<TEntity>(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => await Task.WhenAll(entities.Select(entity => CreateAsync(entity, cancellationToken)));

    /// <summary>
    /// Create a list of entities on the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entities">The entity list.</param>
    /// <remarks>
    /// This is invoking the API without any cancellation support. In order to pass a <see cref="CancellationToken"/>,
    /// you need to use the <see cref="CreateAsync{TEntity}(IEnumerable{TEntity},CancellationToken)"/> overload.
    /// </remarks>
    /// <returns>The created instances of the entities.</returns>
    async Task<IEnumerable<TEntity>> CreateAsync<TEntity>(params TEntity[] entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => await Task.WhenAll(entities.Select(entity => CreateAsync(entity)));

    /// <inheritdoc cref="CreateAsync{TEntity}(TEntity,CancellationToken)"/>
    TEntity Create<TEntity>(TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => CreateAsync(entity).GetAwaiter().GetResult();

    /// <inheritdoc cref="CreateAsync{TEntity}(IEnumerable{TEntity},CancellationToken)"/>
    IEnumerable<TEntity> Create<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => entities.Select(Create);

    /// <inheritdoc cref="CreateAsync{TEntity}(TEntity[])"/>
    IEnumerable<TEntity> Create<TEntity>(params TEntity[] entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => entities.Select(Create);

    /// <summary>
    /// Update (replace) the given entity on the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity instance.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The updated instance of the entity.</returns>
    Task<TEntity> UpdateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <summary>
    /// Update (replace) a list of entities on the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entities">Enumerable of entities.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The updated instances of the entities.</returns>
    async Task<IEnumerable<TEntity>> UpdateAsync<TEntity>(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => await Task.WhenAll(entities.Select(entity => UpdateAsync(entity, cancellationToken)));

    /// <summary>
    /// Update (replace) a list of entities on the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entities">Enumerable of entities.</param>
    /// <remarks>
    /// This is invoking the API without any cancellation support. In order to pass a <see cref="CancellationToken"/>,
    /// you need to use the <see cref="UpdateAsync{TEntity}(IEnumerable{TEntity},CancellationToken)"/> overload.
    /// </remarks>
    /// <returns>The updated instances of the entities.</returns>
    Task<IEnumerable<TEntity>> UpdateAsync<TEntity>(params TEntity[] entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        using var cts = new CancellationTokenSource();
        return UpdateAsync(entities, cts.Token);
    }

    /// <inheritdoc cref="UpdateAsync{TEntity}(TEntity,CancellationToken)"/>
    TEntity Update<TEntity>(TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => UpdateAsync(entity).GetAwaiter().GetResult();

    /// <inheritdoc cref="UpdateAsync{TEntity}(IEnumerable{TEntity},CancellationToken)"/>
    IEnumerable<TEntity> Update<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => entities.Select(Update);

    /// <inheritdoc cref="UpdateAsync{TEntity}(TEntity[])"/>
    IEnumerable<TEntity> Update<TEntity>(params TEntity[] entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => entities.Select(Update);

    /// <summary>
    /// Update the status object of a given entity on the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity that contains a status object.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The entity with the updated status.</returns>
    Task<TEntity> UpdateStatusAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <inheritdoc cref="UpdateStatusAsync{TEntity}"/>
    TEntity UpdateStatus<TEntity>(TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <summary>
    /// Patch a given entity on the Kubernetes API by calculating the diff between the current entity and the provided entity.
    /// This method fetches the current entity from the API, computes the patch, and applies it.
    /// The patch does return the same object if there were no changes detected.
    /// If no operationsFilter is provided, the default filter (<see cref="JsonPatchExtensions.DefaultOperationsFilter"/> is applied.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity containing the desired updates.</param>
    /// <param name="operationsFilter">The filter that is applied to the <see cref="PatchOperation"/>s in the <see cref="JsonPatch"/> to determine if changes are present.</param>
    /// <param name="fieldManager">The field manager name for Server-Side Apply. When specified, enables field tracking for conflict resolution.</param>
    /// <param name="force">Force apply the patch, taking ownership of conflicting fields. Only valid when fieldManager is specified.</param>
    /// <param name="dryRun">When set (e.g., "All"), the patch is validated but not persisted to storage.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>The patched entity.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the entity to be patched does not exist on the API.</exception>
    [RequiresPreviewFeatures("This method is using the JsonPatch feature which is in preview." +
                             "Return values may change (e.g. if the patch was actually applied" +
                             "when no changes were detected. Also, the filtering may not include" +
                             "all volatile properties yet.")]
    Task<TEntity> PatchAsync<TEntity>(
        TEntity entity,
        Func<IReadOnlyList<PatchOperation>, IReadOnlyList<PatchOperation>>? operationsFilter = null,
        string? fieldManager = null,
        bool? force = null,
        string? dryRun = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var currentEntity = Get<TEntity>(entity.Name(), entity.Namespace());
        if (currentEntity is null)
        {
            throw new InvalidOperationException(
                $"Cannot patch entity {typeof(TEntity).Name} with name {entity.Name()} in namespace {entity.Namespace()}: Entity does not exist.");
        }

        return PatchAsync(
            currentEntity,
            entity,
            operationsFilter,
            fieldManager,
            force,
            dryRun,
            cancellationToken);
    }

    /// <summary>
    /// Patch a given entity on the Kubernetes API by calculating the diff between two provided entities.
    /// Returns the patched entity if changes were detected, otherwise returns the original entity.
    /// Detection of changes is done by creating a <see cref="JsonPatch"/> object
    /// and then applying the operationsFilter. Defaults to the <see cref="JsonPatchExtensions.DefaultOperationsFilter"/>.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="from">The current/original entity.</param>
    /// <param name="to">The updated entity with desired changes.</param>
    /// <param name="operationsFilter">The filter that is applied to the <see cref="PatchOperation"/>s in the <see cref="JsonPatch"/> to determine if changes are present.</param>
    /// <param name="fieldManager">The field manager name for Server-Side Apply. When specified, enables field tracking for conflict resolution.</param>
    /// <param name="force">Force apply the patch, taking ownership of conflicting fields. Only valid when fieldManager is specified.</param>
    /// <param name="dryRun">When set (e.g., "All"), the patch is validated but not persisted to storage.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>The patched entity.</returns>
    [RequiresPreviewFeatures("This method is using the JsonPatch feature which is in preview." +
                             "Return values may change (e.g. if the patch was actually applied" +
                             "when no changes were detected. Also, the filtering may not include" +
                             "all volatile properties yet.")]
    Task<TEntity> PatchAsync<TEntity>(
        TEntity from,
        TEntity to,
        Func<IReadOnlyList<PatchOperation>, IReadOnlyList<PatchOperation>>? operationsFilter = null,
        string? fieldManager = null,
        bool? force = null,
        string? dryRun = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        var patch = from.CreateJsonPatch(to, operationsFilter);
        return patch.Operations.Count == 0
            ? Task.FromResult(from)
            : PatchAsync(from, from.CreateJsonPatch(to), fieldManager, force, dryRun, cancellationToken);
    }

    /// <summary>
    /// Patch a given entity on the Kubernetes API using a <see cref="JsonPatch"/> object.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity to patch.</param>
    /// <param name="patch">The <see cref="JsonPatch"/> representing the changes to apply.</param>
    /// <param name="fieldManager">The field manager name for Server-Side Apply. When specified, enables field tracking for conflict resolution.</param>
    /// <param name="force">Force apply the patch, taking ownership of conflicting fields. Only valid when fieldManager is specified.</param>
    /// <param name="dryRun">When set (e.g., "All"), the patch is validated but not persisted to storage.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>The patched entity.</returns>
    [RequiresPreviewFeatures("This method is using the JsonPatch feature which is in preview." +
                             "Return values may change (e.g. if the patch was actually applied" +
                             "when no changes were detected. Also, the filtering may not include" +
                             "all volatile properties yet.")]
    Task<TEntity> PatchAsync<TEntity>(
        TEntity entity,
        JsonPatch patch,
        string? fieldManager = null,
        bool? force = null,
        string? dryRun = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta> =>
        PatchAsync(entity, patch.ToKubernetesPatch(), fieldManager, force, dryRun, cancellationToken);

    /// <summary>
    /// Patch a given entity on the Kubernetes API using a <see cref="V1Patch"/> object.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity to patch.</param>
    /// <param name="patch">The <see cref="V1Patch"/> representing the changes to apply.</param>
    /// <param name="fieldManager">The field manager name for Server-Side Apply. When specified, enables field tracking for conflict resolution.</param>
    /// <param name="force">Force apply the patch, taking ownership of conflicting fields. Only valid when fieldManager is specified.</param>
    /// <param name="dryRun">When set (e.g., "All"), the patch is validated but not persisted to storage.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>The patched entity.</returns>
    Task<TEntity> PatchAsync<TEntity>(
        TEntity entity,
        V1Patch patch,
        string? fieldManager = null,
        bool? force = null,
        string? dryRun = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta> =>
        PatchAsync<TEntity>(patch, entity.Name(), entity.Namespace(), fieldManager, force, dryRun, cancellationToken);

    /// <summary>
    /// Patch a given entity on the Kubernetes API by name and namespace using a <see cref="V1Patch"/> object.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="patch">The <see cref="V1Patch"/> representing the changes to apply.</param>
    /// <param name="name">The name of the entity to patch.</param>
    /// <param name="namespace">The namespace of the entity to patch (if applicable).</param>
    /// <param name="fieldManager">The field manager name for Server-Side Apply. When specified, enables field tracking for conflict resolution.</param>
    /// <param name="force">Force apply the patch, taking ownership of conflicting fields. Only valid when fieldManager is specified.</param>
    /// <param name="dryRun">When set (e.g., "All"), the patch is validated but not persisted to storage.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>The patched entity.</returns>
    Task<TEntity> PatchAsync<TEntity>(
        V1Patch patch,
        string name,
        string? @namespace = null,
        string? fieldManager = null,
        bool? force = null,
        string? dryRun = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <summary>
    /// Patch a given entity on the Kubernetes API by calculating the diff between the current entity and the provided entity.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity containing the desired updates.</param>
    /// <returns>The patched entity.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the entity to be patched does not exist on the API.</exception>
    [RequiresPreviewFeatures("This method is using the JsonPatch feature which is in preview." +
                             "Return values may change (e.g. if the patch was actually applied" +
                             "when no changes were detected. Also, the filtering may not include" +
                             "all volatile properties yet.")]
    TEntity Patch<TEntity>(TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => PatchAsync(entity, operationsFilter: null, cancellationToken: default).GetAwaiter().GetResult();

    /// <summary>
    /// Patch a given entity on the Kubernetes API by calculating the diff between two provided entities.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="from">The current/original entity.</param>
    /// <param name="to">The updated entity with desired changes.</param>
    /// <returns>The patched entity.</returns>
    [RequiresPreviewFeatures("This method is using the JsonPatch feature which is in preview." +
                             "Return values may change (e.g. if the patch was actually applied" +
                             "when no changes were detected. Also, the filtering may not include" +
                             "all volatile properties yet.")]
    TEntity Patch<TEntity>(TEntity from, TEntity to)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => PatchAsync(from, to).GetAwaiter().GetResult();

    /// <summary>
    /// Patch a given entity on the Kubernetes API using a <see cref="JsonPatch"/> object.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity to patch.</param>
    /// <param name="patch">The <see cref="JsonPatch"/> representing the changes to apply.</param>
    /// <param name="fieldManager">The field manager name for Server-Side Apply. When specified, enables field tracking for conflict resolution.</param>
    /// <param name="force">Force apply the patch, taking ownership of conflicting fields. Only valid when fieldManager is specified.</param>
    /// <param name="dryRun">When set (e.g., "All"), the patch is validated but not persisted to storage.</param>
    /// <returns>The patched entity.</returns>
    [RequiresPreviewFeatures("This method is using the JsonPatch feature which is in preview." +
                             "Return values may change (e.g. if the patch was actually applied" +
                             "when no changes were detected. Also, the filtering may not include" +
                             "all volatile properties yet.")]
    TEntity Patch<TEntity>(
        TEntity entity,
        JsonPatch patch,
        string? fieldManager = null,
        bool? force = null,
        string? dryRun = null)
        where TEntity : IKubernetesObject<V1ObjectMeta> =>
        PatchAsync(entity, patch, fieldManager, force, dryRun).GetAwaiter().GetResult();

    /// <summary>
    /// Patch a given entity on the Kubernetes API using a <see cref="V1Patch"/> object.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity to patch.</param>
    /// <param name="patch">The <see cref="V1Patch"/> representing the changes to apply.</param>
    /// <param name="fieldManager">The field manager name for Server-Side Apply. When specified, enables field tracking for conflict resolution.</param>
    /// <param name="force">Force apply the patch, taking ownership of conflicting fields. Only valid when fieldManager is specified.</param>
    /// <param name="dryRun">When set (e.g., "All"), the patch is validated but not persisted to storage.</param>
    /// <returns>The patched entity.</returns>
    TEntity Patch<TEntity>(
        TEntity entity,
        V1Patch patch,
        string? fieldManager = null,
        bool? force = null,
        string? dryRun = null)
        where TEntity : IKubernetesObject<V1ObjectMeta> =>
        PatchAsync(entity, patch, fieldManager, force, dryRun).GetAwaiter().GetResult();

    /// <summary>
    /// Patch a given entity on the Kubernetes API by name and namespace using a <see cref="V1Patch"/> object.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="patch">The <see cref="V1Patch"/> representing the changes to apply.</param>
    /// <param name="name">The name of the entity to patch.</param>
    /// <param name="namespace">The namespace of the entity to patch (if applicable).</param>
    /// <param name="fieldManager">The field manager name for Server-Side Apply. When specified, enables field tracking for conflict resolution.</param>
    /// <param name="force">Force apply the patch, taking ownership of conflicting fields. Only valid when fieldManager is specified.</param>
    /// <param name="dryRun">When set (e.g., "All"), the patch is validated but not persisted to storage.</param>
    /// <returns>The patched entity.</returns>
    TEntity Patch<TEntity>(
        V1Patch patch,
        string name,
        string? @namespace = null,
        string? fieldManager = null,
        bool? force = null,
        string? dryRun = null)
        where TEntity : IKubernetesObject<V1ObjectMeta> =>
        PatchAsync<TEntity>(patch, name, @namespace, fieldManager, force, dryRun).GetAwaiter().GetResult();

    /// <inheritdoc cref="Delete{TEntity}(TEntity)"/>
    /// <returns>A task that completes when the call was made.</returns>
    Task DeleteAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => DeleteAsync<TEntity>(entity.Name(), entity.Namespace(), cancellationToken);

    /// <inheritdoc cref="Delete{TEntity}(IEnumerable{TEntity})"/>
    /// <returns>A task that completes when the call was made.</returns>
    Task DeleteAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => Task.WhenAll(entities.Select(entity => DeleteAsync(entity, cancellationToken)));

    /// <inheritdoc cref="Delete{TEntity}(TEntity[])"/>
    /// <remarks>
    /// This is invoking the API without any cancellation support. In order to pass a <see cref="CancellationToken"/>,
    /// you need to use the <see cref="DeleteAsync{TEntity}(IEnumerable{TEntity},CancellationToken)"/> overload.
    /// </remarks>
    /// <returns>A task that completes when the call was made.</returns>
    Task DeleteAsync<TEntity>(params TEntity[] entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => DeleteAsync(entities, CancellationToken.None);

    /// <inheritdoc cref="Delete{TEntity}(string,string?)"/>
    /// <returns>A task that completes when the call was made.</returns>
    Task DeleteAsync<TEntity>(string name, string? @namespace = null, CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <summary>
    /// Delete a given entity from the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entity">The entity in question.</param>
    void Delete<TEntity>(TEntity entity)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        =>
            Delete<TEntity>(entity.Name(), entity.Namespace());

    /// <summary>
    /// Delete a given list of entities from the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entities">The entities in question.</param>
    void Delete<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        foreach (var entity in entities)
        {
            Delete(entity);
        }
    }

    /// <summary>
    /// Delete a given list of entities from the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="entities">The entities in question.</param>
    void Delete<TEntity>(params TEntity[] entities)
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        foreach (var entity in entities)
        {
            Delete(entity);
        }
    }

    /// <summary>
    /// Delete a given entity by name from the Kubernetes API.
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="name">The name of the entity.</param>
    /// <param name="namespace">The optional namespace of the entity.</param>
    void Delete<TEntity>(string name, string? @namespace = null)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => DeleteAsync<TEntity>(name, @namespace).GetAwaiter().GetResult();

    /// <summary>
    /// Create an entity watcher on the Kubernetes API.
    /// The entity watcher fires events for entity-events on
    /// Kubernetes (events: <see cref="WatchEventType"/>).
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="onEvent">Action that is called when an event occurs.</param>
    /// <param name="onError">Action that handles exceptions.</param>
    /// <param name="onClose">Action that handles closed connections.</param>
    /// <param name="namespace">
    /// The namespace to watch for entities (if needed).
    /// If the namespace is omitted, all entities on the cluster are watched.
    /// </param>
    /// <param name="timeout">The timeout which the watcher has (after this timeout, the server will close the connection).</param>
    /// <param name="allowWatchBookmarks">
    /// Parameter to tell the server to send BOOKMARK events. However, if the server has no implementation or
    /// configuration for bookmarks, this flag is ignored.
    /// </param>
    /// <param name="resourceVersion">
    /// When specified with a watch call, shows changes that occur after that particular version of a resource.
    /// Defaults to changes from the beginning of history.
    /// </param>
    /// <param name="cancellationToken">Cancellation-Token.</param>
    /// <param name="labelSelectors">A list of label-selectors to apply to the search.</param>
    /// <returns>An entity watcher for the given entity.</returns>
    Watcher<TEntity> Watch<TEntity>(
        Action<WatchEventType, TEntity> onEvent,
        Action<Exception>? onError = null,
        Action? onClose = null,
        string? @namespace = null,
        TimeSpan? timeout = null,
        bool? allowWatchBookmarks = null,
        string? resourceVersion = null,
        CancellationToken cancellationToken = default,
        params LabelSelector[] labelSelectors)
        where TEntity : IKubernetesObject<V1ObjectMeta>
        => Watch(
            onEvent,
            onError,
            onClose,
            @namespace,
            timeout,
            allowWatchBookmarks,
            resourceVersion,
            labelSelectors.ToExpression(),
            cancellationToken);

    /// <summary>
    /// Create an entity watcher on the Kubernetes API.
    /// The entity watcher fires events for entity-events on
    /// Kubernetes (events: <see cref="WatchEventType"/>).
    /// </summary>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <param name="onEvent">Action that is called when an event occurs.</param>
    /// <param name="onError">Action that handles exceptions.</param>
    /// <param name="onClose">Action that handles closed connections.</param>
    /// <param name="namespace">
    /// The namespace to watch for entities (if needed).
    /// If the namespace is omitted, all entities on the cluster are watched.
    /// </param>
    /// <param name="timeout">The timeout which the watcher has (after this timeout, the server will close the connection).</param>
    /// <param name="allowWatchBookmarks">
    /// Parameter to tell the server to send BOOKMARK events. However, if the server has no implementation or
    /// configuration for bookmarks, this flag is ignored.
    /// </param>
    /// <param name="resourceVersion">
    /// When specified with a watch call, shows changes that occur after that particular version of a resource.
    /// Defaults to changes from the beginning of history.
    /// </param>
    /// <param name="labelSelector">A string, representing an optional label selector for filtering watched objects.</param>
    /// <param name="cancellationToken">Cancellation-Token.</param>
    /// <returns>An entity watcher for the given entity.</returns>
    Watcher<TEntity> Watch<TEntity>(
        Action<WatchEventType, TEntity> onEvent,
        Action<Exception>? onError = null,
        Action? onClose = null,
        string? @namespace = null,
        TimeSpan? timeout = null,
        bool? allowWatchBookmarks = null,
        string? resourceVersion = null,
        string? labelSelector = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>;

    /// <summary>
    /// Creates an asynchronous entity watcher on the Kubernetes API.
    /// </summary>
    /// <param name="namespace">
    ///  The namespace to watch for entities (if needed).
    /// If the namespace is omitted, all entities on the cluster are watched.
    /// </param>
    /// <param name="resourceVersion">
    /// When specified with a watch call, shows changes that occur after that particular version of a resource.
    /// Defaults to changes from the beginning of history.
    /// </param>
    /// <param name="labelSelector">A string, representing an optional label selector for filtering watched objects.</param>
    /// <param name="allowWatchBookmarks">
    /// Parameter to tell the server to send BOOKMARK events. However, if the server has no implementation or
    /// configuration for bookmarks, this flag is ignored.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
    /// <returns>An asynchronous enumerable that finishes once <paramref name="cancellationToken"/> is cancelled.</returns>
    IAsyncEnumerable<(WatchEventType Type, TEntity Entity)> WatchAsync<TEntity>(
        string? @namespace = null,
        string? resourceVersion = null,
        string? labelSelector = null,
        bool? allowWatchBookmarks = null,
        CancellationToken cancellationToken = default)
        where TEntity : IKubernetesObject<V1ObjectMeta>;
}
