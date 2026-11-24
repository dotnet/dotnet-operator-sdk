// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s;

namespace KubeOps.KubernetesClient;

/// <summary>
/// Builds the <c>User-Agent</c> header under which a client identifies itself to the Kubernetes API server,
/// instead of the <c>kubernetes-csharp/&lt;version&gt;</c> default of the official C# client.
/// </summary>
public static class KubernetesUserAgent
{
    private const string TokenSymbols = "!#$%&'*+-.^_`|~";

    private static readonly string ClientDefault = new KubernetesClientConfiguration().UserAgent;

    private static readonly string KubeOpsVersion = ToToken(WithoutBuildMetadata(
        typeof(KubernetesUserAgent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown"));

    /// <summary>
    /// Creates a user agent for <paramref name="product"/>.
    /// </summary>
    /// <param name="product">The name of the client, e.g. the operator name.</param>
    /// <param name="version">
    /// The version of the client, e.g. <c>1.2.0</c>. Build metadata (from the first <c>+</c> on) is dropped.
    /// Omitted from the user agent when empty.
    /// </param>
    /// <returns>
    /// <c>&lt;product&gt;[/&lt;version&gt;] kubeops/&lt;version&gt; kubernetes-csharp/&lt;version&gt;</c>.
    /// Characters that are not allowed in an HTTP product token (RFC 9110), e.g. spaces or <c>/</c>, are replaced
    /// by <c>-</c>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="product"/> is empty or whitespace.</exception>
    public static string Create(string product, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(product);

        var versionPart = string.IsNullOrWhiteSpace(version)
            ? string.Empty
            : $"/{ToToken(WithoutBuildMetadata(version))}";

        return $"{ToToken(product)}{versionPart} kubeops/{KubeOpsVersion} {ClientDefault}";
    }

    /// <summary>
    /// Sets the user agent from <see cref="Create"/> on <paramref name="config"/>, unless the configuration
    /// already carries an own one. An empty user agent and the default of the C# client count as not configured.
    /// </summary>
    /// <param name="config">The configuration. Set the user agent before a client is created from it.</param>
    /// <param name="product">The name of the client, e.g. the operator name.</param>
    /// <param name="version">The version of the client. Omitted from the user agent when empty.</param>
    /// <exception cref="ArgumentException"><paramref name="product"/> is empty or whitespace.</exception>
    public static void Apply(KubernetesClientConfiguration config, string product, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.UserAgent) || config.UserAgent == ClientDefault)
        {
            config.UserAgent = Create(product, version);
        }
    }

    private static string WithoutBuildMetadata(string version) => version.Split('+')[0];

    private static string ToToken(string value) =>
        string.Concat(value.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || TokenSymbols.Contains(c) ? c : '-'));
}
