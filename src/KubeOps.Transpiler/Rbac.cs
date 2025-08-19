// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s.Models;

using KubeOps.Abstractions.Rbac;

namespace KubeOps.Transpiler;

/// <summary>
/// Transpiler for Kubernetes RBAC attributes to create <see cref="V1PolicyRule"/>s.
/// </summary>
public static class Rbac
{
    /// <summary>
    /// Convert a list of <see cref="RbacAttribute"/>s to a list of <see cref="V1PolicyRule"/>s.
    /// The rules are grouped by entity type and verbs.
    /// </summary>
    /// <param name="context">The <see cref="MetadataLoadContext"/> that was used to load the attributes.</param>
    /// <param name="attributes">List of <see cref="RbacAttribute"/>s.</param>
    /// <returns>A converted, grouped list of <see cref="V1PolicyRule"/>s.</returns>
    public static IEnumerable<V1PolicyRule> Transpile(
        this MetadataLoadContext context,
        IEnumerable<CustomAttributeData> attributes)
    {
        var list = attributes.ToList();

        var generic = list
            .Where(a => a.AttributeType == context.GetContextType<GenericRbacAttribute>())
            .Select(a => new V1PolicyRule
            {
                ApiGroups = a.GetCustomAttributeNamedArrayArg<string>(nameof(GenericRbacAttribute.Groups)),
                Resources = a.GetCustomAttributeNamedArrayArg<string>(nameof(GenericRbacAttribute.Resources)),
                NonResourceURLs = a.GetCustomAttributeNamedArrayArg<string>(nameof(GenericRbacAttribute.Urls)),
                Verbs = ConvertToStrings(
                    a.GetCustomAttributeNamedArg<RbacVerb>(context, nameof(GenericRbacAttribute.Verbs))),
            });

        var entities = list
            .Where(a => a.AttributeType == context.GetContextType<EntityRbacAttribute>())
            .SelectMany(attribute =>
                attribute.GetCustomAttributeCtorArrayArg<Type>(0).Select(type =>
                    (EntityType: type,
                        Verbs: attribute.GetCustomAttributeNamedArg<RbacVerb>(
                            context,
                            nameof(GenericRbacAttribute.Verbs)))))
            .GroupBy(e => e.EntityType)
            .Select(
                group => (
                    Crd: context.ToEntityMetadata(group.Key),
                    Verbs: group.Aggregate(RbacVerb.None, (accumulator, element) => accumulator | element.Verbs)))
            .GroupBy(group => (group.Crd.Metadata.Group, group.Verbs))
            .Select(
                group => new V1PolicyRule
                {
                    ApiGroups = [group.Key.Group],
                    Resources = group.Select(crd => crd.Crd.Metadata.PluralName).Distinct().ToList(),
                    Verbs = ConvertToStrings(group.Key.Verbs),
                });

        var entityStatus = list
            .Where(a => a.AttributeType == context.GetContextType<EntityRbacAttribute>())
            .SelectMany(attribute =>
                attribute.GetCustomAttributeCtorArrayArg<Type>(0).Select(type =>
                    (EntityType: type,
                        Verbs: attribute.GetCustomAttributeNamedArg<RbacVerb>(
                            context,
                            nameof(GenericRbacAttribute.Verbs)))))
            .Where(e => e.EntityType.GetProperty("Status") != null)
            .GroupBy(e => e.EntityType)
            .Select(group => context.ToEntityMetadata(group.Key))
            .Select(
                crd => new V1PolicyRule
                {
                    ApiGroups = [crd.Metadata.Group],
                    Resources = [$"{crd.Metadata.PluralName}/status"],
                    Verbs = ConvertToStrings(RbacVerb.Get | RbacVerb.Patch | RbacVerb.Update),
                });

        return generic.Concat(entities).Concat(entityStatus);
    }

    private static string[] ConvertToStrings(RbacVerb verbs) => verbs switch
    {
        RbacVerb.None => Array.Empty<string>(),
        _ when verbs.HasFlag(RbacVerb.All) => ["*"],
        _ when verbs.HasFlag(RbacVerb.AllExplicit) =>
            Enum.GetValues<RbacVerb>()
                .Where(v => v != RbacVerb.All && v != RbacVerb.None && v != RbacVerb.AllExplicit)
                .Select(v => v.ToString().ToLowerInvariant())
                .ToArray(),
        _ =>
            Enum.GetValues<RbacVerb>()
                .Where(v => verbs.HasFlag(v) && v != RbacVerb.All && v != RbacVerb.None && v != RbacVerb.AllExplicit)
                .Select(v => v.ToString().ToLowerInvariant())
                .ToArray(),
    };
}
