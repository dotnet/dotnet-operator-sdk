// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

namespace KubeOps.KubernetesClient.Test;

#pragma warning disable CA2252 // Opt in to preview features before using them

public sealed class KubernetesClientAsyncTest : IntegrationTestBase, IDisposable
{
    private readonly IKubernetesClient _client =
        new KubernetesClient();

    private readonly IList<V1ConfigMap> _objects = new List<V1ConfigMap>();

    [Fact]
    public async Task Should_Return_Namespace()
    {
        var ns = await _client.GetCurrentNamespaceAsync(cancellationToken: TestContext.Current.CancellationToken);
        ns.Should().Be("default");
    }

    [Fact]
    public async Task Should_Create_Some_Object()
    {
        var config = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "Hello", "World" } },
            },
            TestContext.Current.CancellationToken);

        _objects.Add(config);

        config.Metadata.Should().NotBeNull();
        config.Metadata.ResourceVersion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Should_Get_Some_Object()
    {
        var config = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "Hello", "World" } },
            }, TestContext.Current.CancellationToken);

        _objects.Add(config);
        _objects.Add(await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "Hello", "World" } },
            }, TestContext.Current.CancellationToken));
        _objects.Add(await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "Hello", "World" } },
            }, TestContext.Current.CancellationToken));

        var fetched = await _client.GetAsync<V1ConfigMap>(config.Name(), config.Namespace(), TestContext.Current.CancellationToken);
        fetched!.Name().Should().Be(config.Name());
    }

    [Fact]
    public async Task Should_Update_Some_Object()
    {
        var config = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "Hello", "World" } },
            }, TestContext.Current.CancellationToken);
        var r1 = config.Metadata.ResourceVersion;
        _objects.Add(config);

        config.Data.Add("test", "value");
        config = await _client.UpdateAsync(config, TestContext.Current.CancellationToken);
        var r2 = config.Metadata.ResourceVersion;

        r1.Should().NotBe(r2);
    }

    [Fact]
    public async Task Should_List_Some_Objects()
    {
        var config1 = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "Hello", "World" } },
            }, TestContext.Current.CancellationToken);
        var config2 = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "Hello", "World" } },
            }, TestContext.Current.CancellationToken);

        _objects.Add(config1);
        _objects.Add(config2);

        var configs = await _client.ListAsync<V1ConfigMap>("default", cancellationToken: TestContext.Current.CancellationToken);

        // there are _at least_ 2 config maps (the two that were created)
        configs.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Should_Delete_Some_Object()
    {
        var config1 = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "Hello", "World" } },
            }, TestContext.Current.CancellationToken);
        var config2 = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "Hello", "World" } },
            }, TestContext.Current.CancellationToken);
        _objects.Add(config1);

        var configs = await _client.ListAsync<V1ConfigMap>("default", cancellationToken: TestContext.Current.CancellationToken);
        configs.Count.Should().BeGreaterThanOrEqualTo(2);

        await _client.DeleteAsync(config2, TestContext.Current.CancellationToken);

        configs = await _client.ListAsync<V1ConfigMap>("default", cancellationToken: TestContext.Current.CancellationToken);
        configs.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Should_Not_Throw_On_Not_Found_Delete()
    {
        var config = new V1ConfigMap
        {
            Kind = V1ConfigMap.KubeKind,
            ApiVersion = V1ConfigMap.KubeApiVersion,
            Metadata = new()
            {
                Name = RandomName(),
                NamespaceProperty = "default",
            },
            Data = new Dictionary<string, string> { { "Hello", "World" } },
        };
        await _client.DeleteAsync(config, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Patch_ConfigMap_Async()
    {
        // Add
        var config = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "foo", "bar" } },
            }, TestContext.Current.CancellationToken);
        _objects.Add(config);

        // Add a new key using PatchAsync(config)
        config.Data["hello"] = "world";
        config = await _client.PatchAsync(config, cancellationToken: TestContext.Current.CancellationToken);
        config.Data.Should().ContainKey("hello").And.ContainValue("world");

        // Replace a value using PatchAsync(from, to)
        var from = config;
        var to = new V1ConfigMap
        {
            Kind = V1ConfigMap.KubeKind,
            ApiVersion = V1ConfigMap.KubeApiVersion,
            Metadata = from.Metadata,
            Data = new Dictionary<string, string>
            {
                { "foo", "baz" },
                { "hello", "world" },
            },
        };
        config = await _client.PatchAsync(from, to, cancellationToken: TestContext.Current.CancellationToken);
        config.Data["foo"].Should().Be("baz");

        // Remove a key using PatchAsync(config)
        config.Data.Remove("hello");
        config = await _client.PatchAsync(config, cancellationToken: TestContext.Current.CancellationToken);
        config.Data.Should().NotContainKey("hello");
    }

    [Fact]
    public async Task Should_Patch_ConfigMap_With_Stale_Base_Async()
    {
        // Step 1: Create the ConfigMap
        var original = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "foo", "bar" } },
            }, TestContext.Current.CancellationToken);
        _objects.Add(original);

        // Step 2: Update the ConfigMap via client (simulate external change)
        original.Data["hello"] = "world";
        await _client.UpdateAsync(original, TestContext.Current.CancellationToken);

        // Step 3: Patch using the original object as the base, adding another key
        original.Data["newkey"] = "newvalue";
        var patched = await _client.PatchAsync(original, cancellationToken: TestContext.Current.CancellationToken);

        patched.Data.Should().ContainKey("hello").And.ContainKey("newkey");
        patched.Data["newkey"].Should().Be("newvalue");
    }

    [Fact]
    public async Task Should_Apply_ConfigMap_With_FieldManager_Async()
    {
        // Create a ConfigMap using Server-Side Apply
        var config = new V1ConfigMap
        {
            Kind = V1ConfigMap.KubeKind,
            ApiVersion = V1ConfigMap.KubeApiVersion,
            Metadata = new()
            {
                Name = RandomName(),
                NamespaceProperty = "default",
            },
            Data = new Dictionary<string, string> { { "foo", "bar" } },
        };

        var applied = await _client.ApplyAsync(
            config,
            fieldManager: "test-controller",
            cancellationToken: TestContext.Current.CancellationToken);

        _objects.Add(applied);

        applied.Data.Should().ContainKey("foo").And.ContainValue("bar");
        applied.Metadata.ManagedFields.Should().NotBeNull();
        applied.Metadata.ManagedFields.Should().Contain(f => f.Manager == "test-controller");
    }

    [Fact]
    public async Task Should_Apply_ConfigMap_Updates_Async()
    {
        // Initial apply
        var config = new V1ConfigMap
        {
            Kind = V1ConfigMap.KubeKind,
            ApiVersion = V1ConfigMap.KubeApiVersion,
            Metadata = new()
            {
                Name = RandomName(),
                NamespaceProperty = "default",
            },
            Data = new Dictionary<string, string> { { "initial", "value" } },
        };

        var applied = await _client.ApplyAsync(
            config,
            "test-controller",
            cancellationToken: TestContext.Current.CancellationToken);

        _objects.Add(applied);

        // Update via apply - add a new key
        applied.Data["updated"] = "newvalue";
        var reapplied = await _client.ApplyAsync(
            applied,
            "test-controller",
            cancellationToken: TestContext.Current.CancellationToken);

        reapplied.Data.Should().ContainKey("initial").And.ContainKey("updated");
        reapplied.Data["updated"].Should().Be("newvalue");
    }

    [Fact]
    public async Task Should_Apply_ConfigMap_With_Force_Async()
    {
        // Create ConfigMap with one field manager
        var config = new V1ConfigMap
        {
            Kind = V1ConfigMap.KubeKind,
            ApiVersion = V1ConfigMap.KubeApiVersion,
            Metadata = new()
            {
                Name = RandomName(),
                NamespaceProperty = "default",
            },
            Data = new Dictionary<string, string> { { "shared", "value1" } },
        };

        var applied = await _client.ApplyAsync(
            config,
            "controller-1",
            cancellationToken: TestContext.Current.CancellationToken);

        _objects.Add(applied);

        // Apply with a different field manager using force to take ownership
        applied.Data["shared"] = "value2";
        var forcedApply = await _client.ApplyAsync(
            applied,
            "controller-2",
            force: true,
            cancellationToken: TestContext.Current.CancellationToken);

        forcedApply.Data["shared"].Should().Be("value2");
    }

    [Fact]
    public async Task Should_DryRun_Apply_ConfigMap_Async()
    {
        // Apply with dryRun should validate but not persist
        var config = new V1ConfigMap
        {
            Kind = V1ConfigMap.KubeKind,
            ApiVersion = V1ConfigMap.KubeApiVersion,
            Metadata = new()
            {
                Name = RandomName(),
                NamespaceProperty = "default",
            },
            Data = new Dictionary<string, string> { { "dry", "run" } },
        };

        var dryRunResult = await _client.ApplyAsync(
            config,
            "test-controller",
            dryRun: "All",
            cancellationToken: TestContext.Current.CancellationToken);

        // The dry run should return the object as it would be created
        dryRunResult.Data.Should().ContainKey("dry");

        // But the object should not exist in the cluster
        var fetched = await _client.GetAsync<V1ConfigMap>(
            config.Name(),
            config.Namespace(),
            TestContext.Current.CancellationToken);

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task Should_Patch_ConfigMap_With_FieldManager_Async()
    {
        // Create a ConfigMap
        var config = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "initial", "value" } },
            }, TestContext.Current.CancellationToken);

        _objects.Add(config);

        // Patch with field manager
        config.Data["patched"] = "value";
        var patched = await _client.PatchAsync(
            config,
            operationsFilter: null,
            fieldManager: "test-patch-controller",
            cancellationToken: TestContext.Current.CancellationToken);

        patched.Data.Should().ContainKey("patched").And.ContainValue("value");
        patched.Metadata.ManagedFields.Should().Contain(f => f.Manager == "test-patch-controller");
    }

    [Fact]
    public async Task Should_Clear_ServerManaged_Metadata_Before_Apply_Async()
    {
        // Create a ConfigMap first
        var config = await _client.CreateAsync(
            new V1ConfigMap
            {
                Kind = V1ConfigMap.KubeKind,
                ApiVersion = V1ConfigMap.KubeApiVersion,
                Metadata = new()
                {
                    Name = RandomName(),
                    NamespaceProperty = "default",
                },
                Data = new Dictionary<string, string> { { "initial", "value" } },
            }, TestContext.Current.CancellationToken);

        _objects.Add(config);

        // The created ConfigMap will have server-managed fields populated
        config.Metadata.ResourceVersion.Should().NotBeNullOrEmpty();
        config.Metadata.Uid.Should().NotBeNullOrEmpty();
        config.Metadata.CreationTimestamp.Should().NotBeNull();
        config.Metadata.ManagedFields.Should().NotBeNull().And.NotBeEmpty();

        // Now modify and apply - this should work even with server-managed fields present
        config.Data["updated"] = "newvalue";
        var applied = await _client.ApplyAsync(
            config,
            "test-apply-controller",
            cancellationToken: TestContext.Current.CancellationToken);

        // Verify the apply succeeded
        applied.Data.Should().ContainKey("updated").And.ContainValue("newvalue");
        
        // Verify the returned object has fresh server metadata
        applied.Metadata.ResourceVersion.Should().NotBeNullOrEmpty();
        applied.Metadata.Uid.Should().NotBeNullOrEmpty();
        applied.Metadata.ManagedFields.Should().Contain(f => f.Manager == "test-apply-controller");
    }

    public void Dispose()
    {
        _client.Delete(_objects);
    }

    private static string RandomName()
        => "cm-" + Guid.NewGuid().ToString().ToLower();
}
