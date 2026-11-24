// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Text;

using k8s;

namespace KubeOps.KubernetesClient;

/// <summary>
/// Builds the <c>User-Agent</c> header under which a client identifies itself to the Kubernetes API server.
/// </summary>
/// <remarks>
/// <para>
/// The API server shows the user agent in its audit logs, and it derives the field manager of a write
/// without an explicit <c>fieldManager</c> from the part before the first <c>/</c>. Without an own user agent,
/// every client built on the official C# client identifies itself as <c>kubernetes-csharp/&lt;version&gt;</c>.
/// </para>
/// <para>
/// <see cref="Create"/> returns <c>&lt;product&gt;[/&lt;version&gt;] kubeops/&lt;version&gt;
/// kubernetes-csharp/&lt;version&gt;</c>, e.g. <c>my-operator/1.2.0 kubeops/15.0.0 kubernetes-csharp/21.0.8</c>.
/// </para>
/// </remarks>
public static class KubernetesUserAgent
{
    /// <summary>
    /// Gets the user agent that the official C# client sends unless one is configured,
    /// i.e. <c>kubernetes-csharp/&lt;version&gt;</c>.
    /// </summary>
    public static string ClientDefault { get; } = new KubernetesClientConfiguration().UserAgent;

    private static string KubeOpsVersion { get; } = VersionOf(typeof(KubernetesUserAgent).Assembly) ?? "unknown";

    /// <summary>
    /// Creates a user agent for <paramref name="product"/>.
    /// </summary>
    /// <param name="product">The name of the client, e.g. the operator name.</param>
    /// <param name="version">The version of the client. Omitted from the user agent when empty.</param>
    /// <returns>The user agent, followed by the KubeOps and the C# client version.</returns>
    /// <remarks>
    /// Characters that are not allowed in an HTTP product token (RFC 9110), e.g. spaces or <c>/</c>, are replaced
    /// by <c>-</c>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="product"/> is empty or whitespace.</exception>
    public static string Create(string product, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(product);

        var userAgent = new StringBuilder(ToToken(product));
        if (!string.IsNullOrWhiteSpace(version))
        {
            userAgent.Append('/').Append(ToToken(version));
        }

        return userAgent.Append(" kubeops/").Append(ToToken(KubeOpsVersion)).Append(' ').Append(ClientDefault)
            .ToString();
    }

    /// <summary>
    /// Creates a user agent for <paramref name="product"/> with the version of <paramref name="assembly"/>.
    /// </summary>
    /// <param name="product">The name of the client, e.g. the operator name.</param>
    /// <param name="assembly">
    /// The assembly whose informational version (or, without one, assembly version) is the version of the client.
    /// </param>
    /// <returns>The user agent, followed by the KubeOps and the C# client version.</returns>
    /// <exception cref="ArgumentException"><paramref name="product"/> is empty or whitespace.</exception>
    public static string CreateFromAssembly(string product, Assembly? assembly) =>
        Create(product, assembly is null ? null : VersionOf(assembly));

    /// <summary>
    /// Sets <paramref name="userAgent"/> on <paramref name="config"/> unless the configuration already carries
    /// a user agent other than <see cref="ClientDefault"/>.
    /// </summary>
    /// <param name="config">The configuration of the client. Must be set before the client is created.</param>
    /// <param name="userAgent">The user agent, e.g. from <see cref="Create(string, string?)"/>.</param>
    /// <returns><see langword="true"/> if the user agent was set; otherwise <see langword="false"/>.</returns>
    public static bool SetIfNotConfigured(KubernetesClientConfiguration config, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);

        if (config.UserAgent is not null && config.UserAgent != ClientDefault)
        {
            return false;
        }

        config.UserAgent = userAgent;
        return true;
    }

    private static string? VersionOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString();

    // tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." / "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
    private static string ToToken(string value)
    {
        var trimmed = value.Trim();
        return string.Create(trimmed.Length, trimmed, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = IsTokenChar(source[i]) ? source[i] : '-';
            }
        });
    }

    private static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c);
}
