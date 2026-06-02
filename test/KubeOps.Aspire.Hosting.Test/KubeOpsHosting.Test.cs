// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using FluentAssertions;

namespace KubeOps.Aspire.Hosting.Test;

public class KubeOpsHostingTest
{
    [Fact]
    public void AddKubeOps_Adds_Named_Project_Resource()
    {
        var builder = CreateBuilder();

        builder.AddKubeOps<TestProjectMetadata>("operator-test");

        builder.Resources.OfType<ProjectResource>()
            .Should().ContainSingle(resource => resource.Name == "operator-test");
    }

    [Fact]
    public void AddKubeOps_Returns_Builder_For_Chaining()
    {
        var builder = CreateBuilder();

        var resourceBuilder = builder.AddKubeOps<TestProjectMetadata>("operator-test");

        resourceBuilder.Should().NotBeNull();
        resourceBuilder.Resource.Name.Should().Be("operator-test");
    }

    [Fact]
    public void AddKubeOps_Uses_Explicit_Start_Without_Run_Target()
    {
        var builder = CreateBuilder();

        var resourceBuilder = builder.AddKubeOps<TestProjectMetadata>("operator-test");

        resourceBuilder.Resource.Annotations
            .Should().Contain(annotation => annotation is ExplicitStartupAnnotation);
    }

    [Fact]
    public void AddKubeOps_Adds_Standalone_Publish_Step_Without_Kubernetes_Target()
    {
        var builder = CreateBuilder();

        var resourceBuilder = builder.AddKubeOps<TestProjectMetadata>("operator-test");

        resourceBuilder.Resource.Annotations
            .Should().Contain(annotation => annotation.GetType().FullName == "Aspire.Hosting.Pipelines.PipelineStepAnnotation");
    }

    [Fact]
    public void RunWithKubernetes_Enables_Local_Run_With_Ephemeral_Crds_By_Default()
    {
        var builder = CreateBuilder();
        var kubernetes = builder.AddKubernetesEnvironment("test-k8s");

        var resourceBuilder = builder.AddKubeOps<TestProjectMetadata>("operator-test")
            .RunWithKubernetes(kubernetes);

        resourceBuilder.Resource.Annotations
            .Should().NotContain(annotation => annotation is ExplicitStartupAnnotation);
        resourceBuilder.Resource.Annotations.OfType<KubeOpsRunAnnotation>()
            .Should().ContainSingle()
            .Which.Options.CrdMode.Should().Be(KubeOpsRunCrdMode.Ephemeral);
    }

    [Fact]
    public void RunWithKubernetes_Can_Use_Persistent_Crds()
    {
        var builder = CreateBuilder();
        var kubernetes = builder.AddKubernetesEnvironment("test-k8s");

        var resourceBuilder = builder.AddKubeOps<TestProjectMetadata>("operator-test")
            .RunWithKubernetes(kubernetes, run => run.WithPersistentCrds());

        resourceBuilder.Resource.Annotations.OfType<KubeOpsRunAnnotation>()
            .Should().ContainSingle()
            .Which.Options.CrdMode.Should().Be(KubeOpsRunCrdMode.Persistent);
    }

    [Fact]
    public void PublishAsKubernetesOperator_Configures_Service_Account()
    {
        var builder = CreateBuilder();

        var resourceBuilder = builder.AddKubeOps<TestProjectMetadata>("operator-test")
            .PublishAsKubernetesOperator(publish => publish.WithServiceAccount("operator-sa"));

        resourceBuilder.Resource.Annotations.OfType<KubeOpsPublishAnnotation>()
            .Should().ContainSingle()
            .Which.Options.ServiceAccountName.Should().Be("operator-sa");
    }

    [Fact]
    public void AddKubeOps_Defaults_Published_Service_Account_To_Resource_Name()
    {
        var builder = CreateBuilder();

        var resourceBuilder = builder.AddKubeOps<TestProjectMetadata>("operator-test");

        resourceBuilder.Resource.Annotations.OfType<KubeOpsPublishAnnotation>()
            .Should().ContainSingle()
            .Which.Options.ServiceAccountName.Should().Be("operator-test");
    }

    [Fact]
    public void PublishAsKubernetesOperator_Binds_To_Kubernetes_Environment()
    {
        var builder = CreateBuilder();
        var kubernetes = builder.AddKubernetesEnvironment("publish-k8s");

        var resourceBuilder = builder.AddKubeOps<TestProjectMetadata>("operator-test")
            .PublishAsKubernetesOperator(kubernetes);

        resourceBuilder.Resource.GetComputeEnvironment()
            .Should().BeSameAs(kubernetes.Resource);
    }

    private static IDistributedApplicationTestingBuilder CreateBuilder()
        => global::Aspire.Hosting.Testing.DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AspireAppHost>()
            .GetAwaiter()
            .GetResult();

    private sealed class TestProjectMetadata : IProjectMetadata
    {
        public string ProjectPath => typeof(TestProjectMetadata).Assembly.Location;
    }
}
