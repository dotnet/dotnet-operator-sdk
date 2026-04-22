// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using KubeOps.Abstractions.Builder;

using ZiggyCreatures.Caching.Fusion;

namespace KubeOps.Abstractions.Test.Builder;

public sealed class OperatorSettingsBuilderTest
{
    [Fact]
    public void Build_Produces_Correct_Default_Values()
    {
        var settings = new OperatorSettingsBuilder().Build();

        settings.Name.Should().Be("kubernetesoperator");
        settings.Namespace.Should().BeNull();
        settings.LeaderElectionType.Should().Be(LeaderElectionType.None);
        settings.QueueStrategy.Should().Be(QueueStrategy.InMemory);
        settings.LeaderElectionLeaseDuration.Should().Be(TimeSpan.FromSeconds(15));
        settings.LeaderElectionRenewDeadline.Should().Be(TimeSpan.FromSeconds(10));
        settings.LeaderElectionRetryPeriod.Should().Be(TimeSpan.FromSeconds(2));
        settings.ConfigureResourceWatcherEntityCache.Should().BeNull();
        settings.AutoAttachFinalizers.Should().BeTrue();
        settings.AutoDetachFinalizers.Should().BeTrue();
        settings.ReconcileStrategy.Should().Be(ReconcileStrategy.ByGeneration);
        settings.ParallelReconciliationOptions.Should().NotBeNull();
    }

    [Fact]
    public void Builder_Accepts_All_Property_Setters_And_Passes_Them_Through_Build()
    {
        Action<IFusionCacheBuilder> cacheConfigurator = _ => { };
        var parallelOptions = new ParallelReconciliationOptions { MaxParallelReconciliations = 8 };

        var settings = new OperatorSettingsBuilder
        {
            Name = "my-op",
            Namespace = "my-ns",
            LeaderElectionType = LeaderElectionType.Single,
            QueueStrategy = QueueStrategy.Custom,
            LeaderElectionLeaseDuration = TimeSpan.FromSeconds(30),
            LeaderElectionRenewDeadline = TimeSpan.FromSeconds(20),
            LeaderElectionRetryPeriod = TimeSpan.FromSeconds(5),
            ConfigureResourceWatcherEntityCache = cacheConfigurator,
            AutoAttachFinalizers = false,
            AutoDetachFinalizers = false,
            ReconcileStrategy = ReconcileStrategy.ByResourceVersion,
            ParallelReconciliationOptions = parallelOptions,
        }.Build();

        settings.Name.Should().Be("my-op");
        settings.Namespace.Should().Be("my-ns");
        settings.LeaderElectionType.Should().Be(LeaderElectionType.Single);
        settings.QueueStrategy.Should().Be(QueueStrategy.Custom);
        settings.LeaderElectionLeaseDuration.Should().Be(TimeSpan.FromSeconds(30));
        settings.LeaderElectionRenewDeadline.Should().Be(TimeSpan.FromSeconds(20));
        settings.LeaderElectionRetryPeriod.Should().Be(TimeSpan.FromSeconds(5));
        settings.ConfigureResourceWatcherEntityCache.Should().BeSameAs(cacheConfigurator);
        settings.AutoAttachFinalizers.Should().BeFalse();
        settings.AutoDetachFinalizers.Should().BeFalse();
        settings.ReconcileStrategy.Should().Be(ReconcileStrategy.ByResourceVersion);
        settings.ParallelReconciliationOptions.Should().BeSameAs(parallelOptions);
    }

    [Fact]
    public void Fluent_Api_Sets_All_Properties_And_Builds_Correctly()
    {
        Action<IFusionCacheBuilder> cacheConfigurator = _ => { };
        var parallelOptions = new ParallelReconciliationOptions { MaxParallelReconciliations = 4 };

        var settings = new OperatorSettingsBuilder()
            .WithName("fluent-op")
            .WithNamespace("fluent-ns")
            .WithLeaderElection(LeaderElectionType.Single)
            .WithQueueStrategy(QueueStrategy.Custom)
            .WithLeaderElectionTimings(
                leaseDuration: TimeSpan.FromSeconds(30),
                renewDeadline: TimeSpan.FromSeconds(20),
                retryPeriod: TimeSpan.FromSeconds(5))
            .WithResourceWatcherEntityCaching(cacheConfigurator)
            .WithAutoAttachFinalizers(false)
            .WithAutoDetachFinalizers(false)
            .WithReconcileStrategy(ReconcileStrategy.ByResourceVersion)
            .WithParallelReconciliation(parallelOptions)
            .Build();

        settings.Name.Should().Be("fluent-op");
        settings.Namespace.Should().Be("fluent-ns");
        settings.LeaderElectionType.Should().Be(LeaderElectionType.Single);
        settings.QueueStrategy.Should().Be(QueueStrategy.Custom);
        settings.LeaderElectionLeaseDuration.Should().Be(TimeSpan.FromSeconds(30));
        settings.LeaderElectionRenewDeadline.Should().Be(TimeSpan.FromSeconds(20));
        settings.LeaderElectionRetryPeriod.Should().Be(TimeSpan.FromSeconds(5));
        settings.ConfigureResourceWatcherEntityCache.Should().BeSameAs(cacheConfigurator);
        settings.AutoAttachFinalizers.Should().BeFalse();
        settings.AutoDetachFinalizers.Should().BeFalse();
        settings.ReconcileStrategy.Should().Be(ReconcileStrategy.ByResourceVersion);
        settings.ParallelReconciliationOptions.Should().BeSameAs(parallelOptions);
    }
}
