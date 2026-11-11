// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

using FluentAssertions;

using k8s;
using k8s.Models;

using KubeOps.Cli.Commands.Generator;

using Spectre.Console.Testing;

namespace KubeOps.Cli.Test.Commands.Generator;

[Collection(IntegrationTestCollection.Name)]
[Trait(TestTrait.Category, TestCategory.IntegrationTest)]
[Trait(TestTrait.Area, TestArea.Cli)]
public sealed class OperatorGeneratorIntegrationTest : IAsyncLifetime
{
    private readonly Kubernetes _client = new(KubernetesClientConfiguration.BuildDefaultConfig());
    private readonly DirectoryInfo _output = Directory.CreateTempSubdirectory("kubeops-cli-test-");
    private readonly string _namespace = $"cli-test-{Guid.NewGuid():N}";

    public async ValueTask InitializeAsync() =>
        await _client.CoreV1.CreateNamespaceAsync(
            new V1Namespace { Metadata = new() { Name = _namespace } },
            cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _client.CoreV1.DeleteNamespaceAsync(_namespace, cancellationToken: TestContext.Current.CancellationToken);
        _client.Dispose();
        _output.Delete(true);
    }

    [Theory]
    [InlineData("yaml")]
    [InlineData("json")]
    public async Task Should_Generate_Installer_The_Api_Server_Accepts(string format)
    {
        await Generate("--format", format);

        File.Exists(Path.Join(_output.FullName, $"deployment.{format}")).Should().BeTrue();
        await Kubectl("apply", "--kustomize", _output.FullName, "--dry-run=server");
    }

    [Fact]
    public async Task Should_Point_Webhooks_At_The_Generated_Service()
    {
        await Generate();

        var resources = await Render();
        var service = resources.OfType<V1Service>().Single();
        var validator = resources.OfType<V1ValidatingWebhookConfiguration>().Single().Webhooks.Single();
        var mutator = resources.OfType<V1MutatingWebhookConfiguration>().Single().Webhooks.Single();

        validator.Rules.Single().Operations.Should().Equal("CREATE", "UPDATE");
        validator.ClientConfig.Service.Path.Should().Be("/validate/v1clitestentity");
        mutator.Rules.Single().Operations.Should().Equal("CREATE");
        mutator.ClientConfig.Service.Path.Should().Be("/mutate/v1clitestentity");

        foreach (var clientConfig in new[] { validator.ClientConfig, mutator.ClientConfig })
        {
            clientConfig.Service.Name.Should().Be(service.Name());
            clientConfig.Service.NamespaceProperty.Should().Be(service.Namespace());
            clientConfig.CaBundle.Should().Equal(await File.ReadAllBytesAsync(
                Path.Join(_output.FullName, "ca.pem"),
                TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Should_Issue_Server_Certificate_For_The_Generated_Service()
    {
        await Generate();

        var service = (await Render()).OfType<V1Service>().Single();
        using var ca = await ReadCertificate("ca.pem");
        using var server = await ReadCertificate("svc.pem");
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        server.MatchesHostname($"{service.Name()}.{service.Namespace()}.svc").Should().BeTrue();
        chain.Build(server).Should().BeTrue();
    }

    [Fact]
    public async Task Should_Wire_The_Generated_Deployment()
    {
        await Generate("--docker-image", "registry.test/operator", "--docker-image-tag", "1.2.3");

        var resources = await Render();
        var service = resources.OfType<V1Service>().Single();
        var pod = resources.OfType<V1Deployment>().Single().Spec.Template;
        var container = pod.Spec.Containers.Single();

        container.Image.Should().Be("registry.test/operator:1.2.3");
        pod.Metadata.Labels.Should().Contain(service.Spec.Selector);
        container.Ports.Select(p => p.Name).Should().Contain(service.Spec.Ports.Single().TargetPort.Value);
        resources.OfType<V1Secret>().Select(s => s.Name()).Should()
            .Contain(pod.Spec.Volumes.Select(v => v.Secret.SecretName));
        resources.OfType<V1ConfigMap>().Select(c => c.Name()).Should()
            .Contain(container.EnvFrom.Select(e => e.ConfigMapRef.Name));
    }

    private static async Task<string> Kubectl(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("kubectl", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo)!;
        var cancellationToken = TestContext.Current.CancellationToken;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        process.ExitCode.Should().Be(0, await error);
        return await output;
    }

    private async Task Generate(params string[] options)
    {
        var exitCode = await OperatorGenerator.Handler(
            new TestConsole(),
            OperatorGenerator.Command.Parse(
            [
                "test",
                CliTestOperator.ProjectPath,
                "--out",
                _output.FullName,
                "--namespace",
                _namespace,
                .. options,
            ]));

        exitCode.Should().Be(ExitCodes.Success);
    }

    // What kustomize makes of the generated files is what ends up in the cluster: it applies the name prefix and
    // the namespace, and it hashes the names of the generated secrets and config maps.
    private async Task<List<object>> Render() =>
        KubernetesYaml.LoadAllFromString(await Kubectl("kustomize", _output.FullName));

    private async Task<X509Certificate2> ReadCertificate(string fileName) =>
        X509Certificate2.CreateFromPem(await File.ReadAllTextAsync(
            Path.Join(_output.FullName, fileName),
            TestContext.Current.CancellationToken));
}
