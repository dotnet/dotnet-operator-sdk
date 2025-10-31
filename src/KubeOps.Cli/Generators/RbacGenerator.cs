// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Rbac;
using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;
using KubeOps.Transpiler;

namespace KubeOps.Cli.Generators;

internal sealed class RbacGenerator(MetadataLoadContext parser,
    OutputFormat outputFormat) : IConfigGenerator
{
    public void Generate(ResultOutput output)
    {
        var attributes = parser
            .GetRbacAttributes()
            .Concat(parser.GetContextType<DefaultRbacAttributes>().GetCustomAttributesData<EntityRbacAttribute>())
            .ToList();

        var role = new V1ClusterRole { Rules = parser.Transpile(attributes).ToList() }.Initialize();
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
                        NamespaceProperty = "system",
                    },
                },
            }
            .Initialize();
        roleBinding.Metadata.Name = "operator-role-binding";
        output.Add($"operator-role-binding.{outputFormat.GetFileExtension()}", roleBinding);
    }

    [EntityRbac(typeof(Corev1Event), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Create | RbacVerb.Update)]
    [EntityRbac(typeof(V1Lease), Verbs = RbacVerb.AllExplicit)]
    private sealed class DefaultRbacAttributes;
}
