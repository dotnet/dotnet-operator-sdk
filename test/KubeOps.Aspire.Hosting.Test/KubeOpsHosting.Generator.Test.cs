// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Aspire.Hosting;

using FluentAssertions;

namespace KubeOps.Aspire.Hosting.Test;

[Trait("Area", "Publishing")]
public sealed class KubeOpsHostingGeneratorTest
{
    // Larger than the OS pipe buffer (64 KiB on Linux and macOS), so the CLI blocks while
    // writing stderr unless the caller drains stderr concurrently with stdout.
    private const int StderrBytes = 256 * 1024;

    [Fact]
    public async Task RunKubeOpsGenerator_Does_Not_Block_On_Large_Stderr_Output()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /bin/sh to simulate the KubeOps CLI.");

        var workDir = Directory.CreateTempSubdirectory("kubeops-generator-test");
        try
        {
            var options = new KubeOpsKubernetesManifestOptions()
                .UseKubeOpsCli(
                    "/bin/sh",
                    "-c",
                    $"head -c {StderrBytes} /dev/zero | tr '\\0' x 1>&2; echo done; exit 3",
                    "kubeops");

            var cancellationToken = TestContext.Current.CancellationToken;
            var run = Task.Run(
                () => KubeOpsHostingExtensions.RunKubeOpsGenerator(
                    Path.Combine(workDir.FullName, "Operator.csproj"),
                    Path.Combine(workDir.FullName, "out"),
                    options,
                    "yaml"),
                cancellationToken);

            var act = () => run.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

            var exception = await act.Should().ThrowAsync<InvalidOperationException>();
            exception.Which.Message.Should().StartWith("KubeOps CLI failed with exit code 3.");
            exception.Which.Message.Should().Contain("done");
            exception.Which.Message.Length.Should().BeGreaterThan(StderrBytes);
        }
        finally
        {
            workDir.Delete(true);
        }
    }
}
