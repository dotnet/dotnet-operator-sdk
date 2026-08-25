// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Rbac;
using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;
using KubeOps.Transpiler;

namespace KubeOps.Cli.Generators;

internal sealed class RbacGenerator(
    MetadataLoadContext parser,
    OutputFormat outputFormat,
    string subjectNamespace,
    OperatorWatchScope watchScope) : IConfigGenerator
{
    public void Generate(ResultOutput output)
    {
        var attributes = parser
            .GetRbacAttributes()
            .Concat(parser.GetContextType<DefaultRbacAttributes>().GetCustomAttributesData<EntityRbacAttribute>())
            .ToList();
        var rules = parser.Transpile(attributes)
            .OrderBy(r => r.ApiGroups?.FirstOrDefault() ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(r => r.Resources?.FirstOrDefault() ?? r.NonResourceURLs?.FirstOrDefault() ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        if (watchScope.Kind == OperatorWatchScopeKind.Namespaced)
        {
            ValidateNamespacedRules(attributes, rules);
            GenerateNamespaced(output, rules);
            return;
        }

        GenerateClusterWide(output, rules);
    }

    internal static V1Role CreateNamespacedRole(IList<V1PolicyRule> rules, string @namespace)
    {
        var role = new V1Role { Rules = rules }.Initialize();
        role.Metadata.Name = "operator-role";
        role.Metadata.NamespaceProperty = @namespace;
        return role;
    }

    internal static V1RoleBinding CreateNamespacedRoleBinding(string @namespace)
    {
        var roleBinding = new V1RoleBinding
        {
            RoleRef = new()
            {
                ApiGroup = V1Role.KubeGroup,
                Kind = V1Role.KubeKind,
                Name = "operator-role",
            },
            Subjects = new List<Rbacv1Subject>
            {
                new()
                {
                    Kind = V1ServiceAccount.KubeKind,
                    Name = "default",
                    NamespaceProperty = @namespace,
                },
            },
        }
        .Initialize();
        roleBinding.Metadata.Name = "operator-role-binding";
        roleBinding.Metadata.NamespaceProperty = @namespace;
        return roleBinding;
    }

    private void GenerateNamespaced(ResultOutput output, IList<V1PolicyRule> rules)
    {
        var role = CreateNamespacedRole(rules, subjectNamespace);
        output.Add($"operator-role.{outputFormat.GetFileExtension()}", role);

        var roleBinding = CreateNamespacedRoleBinding(subjectNamespace);
        output.Add($"operator-role-binding.{outputFormat.GetFileExtension()}", roleBinding);
    }

    private void GenerateClusterWide(ResultOutput output, IList<V1PolicyRule> rules)
    {
        var role = new V1ClusterRole { Rules = rules }.Initialize();
        role.Metadata.Name = "operator-role";
        output.Add($"operator-role.{outputFormat.GetFileExtension()}", role);

        var roleBinding = new V1ClusterRoleBinding
        {
            RoleRef = new()
            {
                ApiGroup = V1ClusterRole.KubeGroup,
                Kind = V1ClusterRole.KubeKind,
                Name = "operator-role",
            },
            Subjects = new List<Rbacv1Subject>
            {
                new()
                {
                    Kind = V1ServiceAccount.KubeKind,
                    Name = "default",
                    NamespaceProperty = subjectNamespace,
                },
            },
        }
        .Initialize();
        roleBinding.Metadata.Name = "operator-role-binding";
        output.Add($"operator-role-binding.{outputFormat.GetFileExtension()}", roleBinding);
    }

    private void ValidateNamespacedRules(
        IEnumerable<CustomAttributeData> attributes,
        IEnumerable<V1PolicyRule> rules)
    {
        var entityRbacAttribute = parser.GetContextType<EntityRbacAttribute>();
        var clusterScopedEntities = attributes
            .Where(attribute => attribute.AttributeType == entityRbacAttribute)
            .SelectMany(attribute => attribute.GetCustomAttributeCtorArrayArg<Type>(0))
            .Where(type => parser.ToEntityMetadata(type).Scope == nameof(EntityScope.Cluster))
            .Select(type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (clusterScopedEntities.Count > 0)
        {
            throw new InvalidOperationException(
                "Namespaced operator RBAC cannot grant access to cluster-scoped entities: " +
                string.Join(", ", clusterScopedEntities));
        }

        if (rules.Any(rule => rule.NonResourceURLs is { Count: > 0 }))
        {
            throw new InvalidOperationException(
                "Namespaced operator RBAC cannot grant access to non-resource URLs.");
        }
    }

    [EntityRbac(typeof(Corev1Event), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Create | RbacVerb.Update)]
    [EntityRbac(typeof(V1Lease), Verbs = RbacVerb.AllExplicit)]
    private sealed class DefaultRbacAttributes;
}
