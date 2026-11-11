// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.TestTraits;

/// <summary>Trait names. Every test has one <see cref="Category"/> and one <see cref="Area"/>.</summary>
public static class TestTrait
{
    public const string Category = "Category";

    public const string Area = "Area";

    public const string Feature = "Feature";
}

/// <summary>Values of <see cref="TestTrait.Category"/>; they match the test class suffix.</summary>
public static class TestCategory
{
    public const string UnitTest = "UnitTest";

    public const string IntegrationTest = "IntegrationTest";
}

/// <summary>Values of <see cref="TestTrait.Area"/>: the part of the SDK a test covers.</summary>
public static class TestArea
{
    public const string Aspire = "Aspire";

    public const string Cli = "Cli";

    public const string Configuration = "Configuration";

    public const string CrdSchema = "CrdSchema";

    public const string Crds = "Crds";

    public const string Events = "Events";

    public const string Finalizers = "Finalizers";

    public const string JsonPatch = "JsonPatch";

    public const string KubernetesClient = "KubernetesClient";

    public const string LeaderElection = "LeaderElection";

    public const string Logging = "Logging";

    public const string Metrics = "Metrics";

    public const string Queue = "Queue";

    public const string Rbac = "Rbac";

    public const string Reconciliation = "Reconciliation";

    public const string Retry = "Retry";

    public const string Selectors = "Selectors";

    public const string SourceGenerator = "SourceGenerator";

    public const string TestSuite = "TestSuite";

    public const string Watcher = "Watcher";

    public const string Webhooks = "Webhooks";
}

/// <summary>Values of <see cref="TestTrait.Feature"/>: capabilities that span several areas.</summary>
public static class TestFeature
{
    public const string LeaderLoss = "LeaderLoss";

    public const string MultipleControllers = "MultipleControllers";

    public const string Otel = "Otel";

    public const string ScopedLeaderElection = "ScopedLeaderElection";
}
