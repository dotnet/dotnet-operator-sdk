// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

var builder = DistributedApplication.CreateBuilder(args);

var k8s = builder.AddKubernetesEnvironment("k8s")
    .WithHelm(helm =>
    {
        helm.WithChartName("kubeops-aspire");
        helm.WithReleaseName("kubeops-aspire");
        helm.WithNamespace("operator-system");
    });

builder.AddKubeOps<Projects.AspireOperator>("operator")
    .RunWithKubernetes(k8s, run => run.WithPersistentCrds())
    .PublishAsKubernetesOperator(
        k8s,
        manifests =>
        {
            manifests.Namespace = "operator-system";
            manifests.UseKubeOpsCli(
                "dotnet",
                "run",
                "--project",
                "..\\..\\src\\KubeOps.Cli\\KubeOps.Cli.csproj",
                "--framework",
                "net10.0",
                "--");
        });

builder.Build().Run();
