// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Builder;
using KubeOps.Operator.Builder;

using Microsoft.Extensions.DependencyInjection;

namespace KubeOps.Operator;

/// <summary>
/// Method extensions for the <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the Kubernetes operator to the dependency injection.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/>.</param>
    /// <param name="configure">An optional configure action for adjusting settings in the operator.</param>
    /// <returns>An <see cref="IOperatorBuilder"/> for further configuration and chaining.</returns>
    public static IOperatorBuilder AddKubernetesOperator(
        this IServiceCollection services,
        Action<OperatorSettings>? configure = null)
    {
        var settings = new OperatorSettings();
        configure?.Invoke(settings);
        return new OperatorBuilder(services, settings);
    }
}
