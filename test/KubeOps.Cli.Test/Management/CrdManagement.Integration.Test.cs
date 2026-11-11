// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;

using FluentAssertions;

using k8s;
using k8s.Autorest;
using k8s.Models;

using KubeOps.Cli.Commands.Management;

using Spectre.Console.Testing;

namespace KubeOps.Cli.Test.Management;

[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "CrdManagement")]
public sealed class CrdManagementIntegrationTest : IAsyncLifetime
{
    private readonly Kubernetes _client = new(KubernetesClientConfiguration.BuildDefaultConfig());

    public async ValueTask InitializeAsync() => await DeleteCrd();

    public async ValueTask DisposeAsync()
    {
        await DeleteCrd();
        _client.Dispose();
    }

    [Fact]
    public async Task Should_Install_Crds_In_Cluster()
    {
        var exitCode = await Install.Handler(
            new TestConsole(),
            _client,
            Install.Command.Parse([CliTestOperator.ProjectPath]));

        exitCode.Should().Be(ExitCodes.Success);
        var crd = await GetCrd();
        crd.Should().NotBeNull();
        crd.Spec.Names.Kind.Should().Be("CliTestEntity");
    }

    [Fact]
    public async Task Should_Overwrite_Existing_Crds_When_Forced()
    {
        await Install.Handler(new TestConsole(), _client, Install.Command.Parse([CliTestOperator.ProjectPath]));
        await WaitUntilCrdIsEstablished();
        await _client.ApiextensionsV1.PatchCustomResourceDefinitionAsync(
            new V1Patch("""{"metadata":{"labels":{"changed":"true"}}}""", V1Patch.PatchType.MergePatch),
            CliTestOperator.CrdName,
            cancellationToken: TestContext.Current.CancellationToken);

        var exitCode = await Install.Handler(
            new TestConsole(),
            _client,
            Install.Command.Parse([CliTestOperator.ProjectPath, "--force"]));

        exitCode.Should().Be(ExitCodes.Success);
        var overwritten = await GetCrd();
        overwritten!.Metadata.Labels.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Should_Uninstall_Crds_From_Cluster()
    {
        await Install.Handler(new TestConsole(), _client, Install.Command.Parse([CliTestOperator.ProjectPath]));

        var exitCode = await Uninstall.Handler(
            new TestConsole(),
            _client,
            Uninstall.Command.Parse([CliTestOperator.ProjectPath, "--force"]));

        exitCode.Should().Be(ExitCodes.Success);
        await WaitUntilCrdIsGone();
    }

    private async Task<V1CustomResourceDefinition?> GetCrd()
    {
        try
        {
            return await _client.ApiextensionsV1.ReadCustomResourceDefinitionAsync(
                CliTestOperator.CrdName,
                cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task DeleteCrd()
    {
        try
        {
            await _client.ApiextensionsV1.DeleteCustomResourceDefinitionAsync(
                CliTestOperator.CrdName,
                cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await WaitUntilCrdIsGone();
    }

    // A CRD is removed asynchronously and blocks the installation of its successor until then.
    private Task WaitUntilCrdIsGone() => WaitUntil(crd => crd is null);

    // The API server keeps updating the status of a new CRD, which conflicts with an overwrite based on a stale read.
    private Task WaitUntilCrdIsEstablished() => WaitUntil(crd =>
        crd?.Status?.Conditions?.Any(c => c is { Type: "Established", Status: "True" }) == true);

    private async Task WaitUntil(Func<V1CustomResourceDefinition?, bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (!condition(await GetCrd()))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }
}
