using System.Text;
using System.Text.Json;
using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using Asgard.Hermod.ObjectStorage.Abstractions.Models;
using Asgard.Hermod.Redis;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpDefaultAccessTests
{
    private static readonly McpTarget Storage = new("storage-a", "Objects A", "storage", "v1");
    private static readonly McpTarget Disabled = new("storage-b", "Objects B", "storage", "v1");

    [Fact]
    public async Task DefaultsToAllInstancesFiltersExclusionsAndConfirmsTheInstanceOnce()
    {
        var prompts = new ApprovalStub();
        var client = ProtocolStub<IObjectStorageClient>.For((name, args) => name switch
        {
            nameof(IObjectStorageClient.ListBucketsAsync) => Task.FromResult<IReadOnlyList<ObjectStorageBucket>>(
                [new("exports", "region"), new("private-bucket", "region")]),
            nameof(IObjectStorageClient.ListObjectsAsync) => Task.FromResult(new ObjectStoragePage(
                [new("public/report.txt", false), new("secret/password.txt", false)])),
            nameof(IObjectStorageClient.OpenReadAsync) => Task.FromResult(new ObjectStorageReadResult(new MemoryStream(Encoding.UTF8.GetBytes("hello")), 5)),
            _ => throw new InvalidOperationException(name),
        });
        using var rig = new McpTestRig([Storage, Disabled], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client),
            defaultAccess: true, approval: prompts);
        await rig.Access.SetExclusionsAsync("", [new("", Disabled.Id, Disabled.TypeId),
            new("", Storage.Id, Storage.TypeId, "private-bucket"), new("", Storage.Id, Storage.TypeId, "exports", "secret/")], McpTestRig.Token);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        var listing = Result(await Call(rig, session, "hermod_list_instances"));
        var instance = Assert.Single(listing.GetProperty("instances").EnumerateArray());
        Assert.Equal(Storage.Id, instance.GetProperty("instance_id").GetString());
        string grantId = instance.GetProperty("grant_id").GetString()!;
        Assert.True(instance.GetProperty("scope").GetProperty("all_resources").GetBoolean());

        var resources = Result(await Call(rig, session, McpToolCatalog.ListResources, ("grant_id", grantId)));
        Assert.Equal("exports", Assert.Single(resources.GetProperty("items").EnumerateArray()).GetString());
        Assert.Single(prompts.Requests);
        var objects = Result(await Call(rig, session, "hermod_storage_list", ("grant_id", grantId), ("bucket", "exports")));
        Assert.Equal("public/report.txt", Assert.Single(objects.GetProperty("entries").EnumerateArray()).GetProperty("key").GetString());
        Assert.Single(prompts.Requests);
        Assert.Equal("Objects A", prompts.Requests[0].InstanceName);
        Assert.True(prompts.Requests[0].ShowDownloadFolder);
        var text = Result(await Call(rig, session, "hermod_storage_read_text", ("grant_id", grantId), ("bucket", "exports"), ("key", "public/report.txt")));
        Assert.Equal("hello", text.GetProperty("content").GetString());
        Assert.Single(prompts.Requests);
        Assert.True((await Call(rig, session, "hermod_storage_read_text", ("grant_id", grantId), ("bucket", "exports"), ("key", "secret/password.txt"))).IsError);
        Assert.Single(prompts.Requests);

        rig.Store.Targets = [Storage, Disabled, new("new-sql", "New SQL", "mysql", "v1")];
        var after = Result(await Call(rig, session, "hermod_list_instances"));
        Assert.Equal(2, after.GetProperty("instances").GetArrayLength());
        Assert.Contains(after.GetProperty("instances").EnumerateArray(), item => item.GetProperty("instance_id").GetString() == "new-sql");
    }

    [Fact]
    public async Task OneInstanceConfirmationCoversOtherBucketsAndDeletionButNotAnotherConnection()
    {
        var prompts = new ApprovalStub();
        var deleted = new List<string>();
        var client = ProtocolStub<IObjectStorageClient>.For((name, args) => name switch
        {
            nameof(IObjectStorageClient.ListObjectsAsync) => Task.FromResult(new ObjectStoragePage([])),
            nameof(IObjectStorageClient.DeleteAsync) => Delete(args),
            _ => throw new InvalidOperationException(name),
        });
        using var rig = new McpTestRig([Storage, Disabled], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client),
            defaultAccess: true, approval: prompts);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        string id = McpDefaultAccess.Id(session, Storage);
        Result(await Call(rig, session, "hermod_storage_list", ("grant_id", id), ("bucket", "exports"), ("prefix", "first/")));
        Result(await Call(rig, session, "hermod_storage_list", ("grant_id", id), ("bucket", "other"), ("prefix", "second/")));
        var plan = Result(await Call(rig, session, McpToolCatalog.StoragePrepareDelete,
            ("grant_id", id), ("bucket", "other"), ("keys", new[] { "second/file.txt" })));
        string planId = plan.GetProperty("plan_id").GetString()!;
        Result(await Call(rig, session, McpToolCatalog.StorageDeletePlan, ("grant_id", id), ("bucket", "other"), ("plan_id", planId)));
        Result(await Call(rig, session, McpToolCatalog.StorageExecuteDelete, ("grant_id", id), ("bucket", "other"), ("plan_id", planId)));
        Assert.Equal(["second/file.txt"], deleted);
        Assert.Single(prompts.Requests);

        Result(await Call(rig, session, "hermod_storage_list", ("grant_id", McpDefaultAccess.Id(session, Disabled)), ("bucket", "exports")));
        Assert.Equal(2, prompts.Requests.Count);
        var nextSession = rig.Session;
        Result(await Call(rig, nextSession, "hermod_storage_list", ("grant_id", McpDefaultAccess.Id(nextSession, Storage)), ("bucket", "exports")));
        Assert.Equal(3, prompts.Requests.Count);

        Task Delete(object?[] args)
        {
            deleted.AddRange((IEnumerable<string>)args[1]!);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task OfficialMcpClientDiscoversInstancesAndRequestsAConfirmedBucket()
    {
        var prompts = new ApprovalStub();
        var client = ProtocolStub<IObjectStorageClient>.For((name, _) => name == nameof(IObjectStorageClient.ListObjectsAsync)
            ? Task.FromResult(new ObjectStoragePage([new("daily/report.txt", false)])) : throw new InvalidOperationException(name));
        using var rig = new McpTestRig([Storage], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client),
            defaultAccess: true, approval: prompts);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string pipeName = "Hermod.Mcp.Tests." + Guid.NewGuid().ToString("N");
        await using var server = new McpDesktopServer(rig.Access, new(rig.Tools), name: pipeName);
        server.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(McpTestRig.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        await using var pipe = await McpPipeEndpoint.ConnectAsync(pipeName, token);
        Assert.True(await McpPipeEndpoint.AuthenticateAsync(pipe, rig.Session.Policy.Secret, token));
        await using var sdk = await McpClient.CreateAsync(new StreamClientTransport(pipe, pipe), cancellationToken: token);
        var tools = await sdk.ListToolsAsync(cancellationToken: token);
        Assert.Contains(tools, tool => tool.Name == McpToolCatalog.ListResources);
        var storageTool = Assert.Single(tools, tool => tool.Name == "hermod_storage_list");
        Assert.True(storageTool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("bucket", out _));
        var instances = Result(await sdk.CallToolAsync("hermod_list_instances", new Dictionary<string, object?>(), cancellationToken: token));
        string id = Assert.Single(instances.GetProperty("instances").EnumerateArray()).GetProperty("grant_id").GetString()!;
        var result = Result(await sdk.CallToolAsync("hermod_storage_list", new Dictionary<string, object?>
        {
            ["grant_id"] = id,
            ["bucket"] = "exports",
            ["prefix"] = "daily/",
        }, cancellationToken: token));
        Assert.Equal("daily/report.txt", Assert.Single(result.GetProperty("entries").EnumerateArray()).GetProperty("key").GetString());
        var request = Assert.Single(prompts.Requests);
        Assert.Equal("Objects A", request.InstanceName);
        Assert.Contains(request.Fields, field => field.Value == "exports");
        Assert.Contains(request.Fields, field => field.Value == "daily/");
    }

    [Fact]
    public async Task DenialOrPolicyChangePreventsConnectionAndLateApprovalCannotRun()
    {
        var waiting = new TaskCompletionSource<McpApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompts = new ApprovalStub(async (_, token) => { entered.SetResult(); return await waiting.Task.WaitAsync(token); });
        int opened = 0;
        using var rig = new McpTestRig([Storage], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) =>
        { opened++; return ProtocolStub<IObjectStorageClient>.For(); }), defaultAccess: true, approval: prompts);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        string id = McpDefaultAccess.Id(session, Storage);
        var pending = Call(rig, session, "hermod_storage_list", ("grant_id", id), ("bucket", "exports"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), McpTestRig.Token);
        await rig.Access.SetExclusionsAsync("", [new("", Storage.Id, Storage.TypeId)], McpTestRig.Token);
        waiting.TrySetResult(new(true));
        Assert.True((await pending).IsError);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task MissingOrDeniedConfirmationNeverOpensTheInstance()
    {
        int opened = 0;
        var factory = ProtocolStub<IObjectStorageClientFactory>.For((_, _) => { opened++; return ProtocolStub<IObjectStorageClient>.For(); });
        using var unavailable = new McpTestRig([Storage], storage: factory, defaultAccess: true);
        await unavailable.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var firstSession = unavailable.Session;
        var first = await Call(unavailable, firstSession, "hermod_storage_list", ("grant_id", McpDefaultAccess.Id(firstSession, Storage)), ("bucket", "exports"));
        Assert.True(first.IsError);
        var denied = new ApprovalStub((_, _) => Task.FromResult(new McpApprovalResult(false)));
        using var rejected = new McpTestRig([Storage], storage: factory, defaultAccess: true, approval: denied);
        await rejected.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var secondSession = rejected.Session;
        var second = await Call(rejected, secondSession, "hermod_storage_list", ("grant_id", McpDefaultAccess.Id(secondSession, Storage)), ("bucket", "exports"));
        Assert.True(second.IsError);
        Assert.Single(denied.Requests);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task ConcurrentRequestsForTheSameInstanceShareOneConfirmation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approved = new TaskCompletionSource<McpApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompts = new ApprovalStub(async (_, token) => { entered.TrySetResult(); return await approved.Task.WaitAsync(token); });
        var client = ProtocolStub<IObjectStorageClient>.For((_, _) => Task.FromResult(new ObjectStoragePage([])));
        using var rig = new McpTestRig([Storage], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client),
            defaultAccess: true, approval: prompts);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        string id = McpDefaultAccess.Id(session, Storage);
        var first = Call(rig, session, "hermod_storage_list", ("grant_id", id), ("bucket", "exports"), ("prefix", "reports/"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), McpTestRig.Token);
        var second = Call(rig, session, "hermod_storage_list", ("grant_id", id), ("bucket", "other-bucket"), ("prefix", "other/"));
        approved.SetResult(new(true));
        Assert.False((await first).IsError == true);
        Assert.False((await second).IsError == true);
        Assert.Single(prompts.Requests);
    }

    [Fact]
    public async Task ChangedConnectionRequiresANewConfirmationInTheSameSession()
    {
        var prompts = new ApprovalStub();
        var client = ProtocolStub<IObjectStorageClient>.For((_, _) => Task.FromResult(new ObjectStoragePage([])));
        using var rig = new McpTestRig([Storage], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client),
            defaultAccess: true, approval: prompts);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        string id = McpDefaultAccess.Id(session, Storage);
        Result(await Call(rig, session, "hermod_storage_list", ("grant_id", id), ("bucket", "exports")));
        Assert.Single(prompts.Requests);
        var changed = Storage with { Revision = "v2" };
        rig.Store.Targets = [changed];
        Assert.True((await Call(rig, session, "hermod_storage_list", ("grant_id", id), ("bucket", "exports"))).IsError);
        string newId = McpDefaultAccess.Id(session, changed);
        Result(await Call(rig, session, "hermod_storage_list", ("grant_id", newId), ("bucket", "exports")));
        Assert.Equal(2, prompts.Requests.Count);
    }

    [Fact]
    public async Task DownloadPromptsForDestinationAndNeedsNoPreconfiguredGrant()
    {
        using var folder = new TemporaryFolder();
        var prompts = new ApprovalStub((_, _) => Task.FromResult(new McpApprovalResult(true, folder.Path)));
        var client = ProtocolStub<IObjectStorageClient>.For((name, _) => name == nameof(IObjectStorageClient.OpenReadAsync)
            ? Task.FromResult(new ObjectStorageReadResult(new MemoryStream([1, 2, 3]), 3)) : throw new InvalidOperationException(name));
        using var rig = new McpTestRig([Storage], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client),
            defaultAccess: true, approval: prompts);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        string id = McpDefaultAccess.Id(session, Storage);
        var invalid = await Call(rig, session, McpToolCatalog.StorageStartTransfer, ("grant_id", id), ("bucket", "exports"),
            ("direction", "download"), ("key", "reports/result.bin"), ("local_path", "relative/result.bin"));
        Assert.True(invalid.IsError);
        Assert.Empty(prompts.Requests);
        var queued = Result(await Call(rig, session, McpToolCatalog.StorageStartTransfer, ("grant_id", id), ("bucket", "exports"),
            ("direction", "download"), ("key", "reports/result.bin")));
        var prompt = Assert.Single(prompts.Requests);
        Assert.True(prompt.RequiresDownloadFolder);
        Assert.Equal("result.bin", prompt.DownloadFileName);
        string task = queued.GetProperty("task_id").GetString()!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(McpTestRig.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            var state = Result(await Call(rig, session, McpToolCatalog.StorageTransferStatus, ("task_id", task)));
            if (state.GetProperty("status").GetString() is "completed" or "failed")
            {
                Assert.Equal("completed", state.GetProperty("status").GetString());
                break;
            }
            await Task.Delay(15, deadline.Token);
        }
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(folder.Path, "result.bin"), McpTestRig.Token));
    }

    [Fact]
    public async Task RecursiveTransferCannotIncludeAProhibitedChildFolder()
    {
        var prompts = new ApprovalStub();
        int opened = 0;
        using var rig = new McpTestRig([Storage], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) =>
        { opened++; return ProtocolStub<IObjectStorageClient>.For(); }), defaultAccess: true, approval: prompts);
        await rig.Access.SetExclusionsAsync("", [new("", Storage.Id, Storage.TypeId, "exports", "reports/private/")], McpTestRig.Token);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        foreach (string key in new[] { "reports/", "/" })
        {
            var blocked = await Call(rig, session, McpToolCatalog.StorageStartTransfer,
                ("grant_id", McpDefaultAccess.Id(session, Storage)), ("bucket", "exports"), ("direction", "download"), ("key", key));
            Assert.True(blocked.IsError);
        }
        Assert.Empty(prompts.Requests);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task WholeBucketTransfersUseRootAndKeepTheDirectoryHierarchy()
    {
        using var destination = new TemporaryFolder();
        using var alternate = new TemporaryFolder();
        using var source = new TemporaryFolder();
        string nested = Directory.CreateDirectory(Path.Combine(source.Path, "nested")).FullName;
        await File.WriteAllTextAsync(Path.Combine(nested, "upload.txt"), "upload", McpTestRig.Token);
        var prompts = new ApprovalStub((_, _) => Task.FromResult(new McpApprovalResult(true, destination.Path)));
        var uploaded = new List<string>();
        var client = ProtocolStub<IObjectStorageClient>.For((name, args) => name switch
        {
            nameof(IObjectStorageClient.ListBucketsAsync) => Task.FromResult<IReadOnlyList<ObjectStorageBucket>>([new("exports", "region")]),
            nameof(IObjectStorageClient.ListObjectsAsync) => ListObjects(args),
            nameof(IObjectStorageClient.OpenReadAsync) => Task.FromResult(new ObjectStorageReadResult(new MemoryStream([1, 2, 3]), 3)),
            nameof(IObjectStorageClient.UploadAsync) => Upload(args),
            _ => throw new InvalidOperationException(name),
        });
        using var rig = new McpTestRig([Storage], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client),
            defaultAccess: true, approval: prompts);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        string id = McpDefaultAccess.Id(session, Storage);
        Result(await Call(rig, session, McpToolCatalog.ListResources, ("grant_id", id)));
        Assert.True(Assert.Single(prompts.Requests).ShowDownloadFolder);
        var download = Result(await Call(rig, session, McpToolCatalog.StorageStartTransfer, ("grant_id", id), ("bucket", "exports"),
            ("direction", "download"), ("key", "/")));
        await WaitAsync(download);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(destination.Path, "nested", "result.bin"), McpTestRig.Token));
        string explicitPath = Path.Combine(alternate.Path, "renamed.bin");
        var explicitDownload = Result(await Call(rig, session, McpToolCatalog.StorageStartTransfer, ("grant_id", id), ("bucket", "exports"),
            ("direction", "download"), ("key", "other.bin"), ("local_path", explicitPath)));
        await WaitAsync(explicitDownload);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(explicitPath, McpTestRig.Token));
        var upload = Result(await Call(rig, session, McpToolCatalog.StorageStartTransfer, ("grant_id", id), ("bucket", "exports"),
            ("direction", "upload"), ("key", "/"), ("local_path", source.Path)));
        await WaitAsync(upload);
        Assert.Equal(["nested/upload.txt"], uploaded);
        Assert.Single(prompts.Requests);
        Assert.False(prompts.Requests[0].RequiresDownloadFolder);

        Task<ObjectStoragePage> ListObjects(object?[] args)
        {
            Assert.Equal("", args[1]);
            return Task.FromResult(new ObjectStoragePage([new("nested/result.bin", false, 3)]));
        }
        Task Upload(object?[] args)
        {
            uploaded.Add((string)args[1]!);
            return Task.CompletedTask;
        }
        async Task WaitAsync(JsonElement queued)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(McpTestRig.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                var state = Result(await Call(rig, session, McpToolCatalog.StorageTransferStatus,
                    ("task_id", queued.GetProperty("task_id").GetString()!)));
                if (state.GetProperty("status").GetString() is "completed" or "failed")
                {
                    Assert.Equal("completed", state.GetProperty("status").GetString());
                    return;
                }
                await Task.Delay(15, deadline.Token);
            }
        }
    }

    [Fact]
    public async Task ConfirmedRedisKeyReturnsTextAndBinaryValuesWithBoundOpaquePaging()
    {
        var target = new McpTarget("redis", "Redis", "redis", "v1");
        int reads = 0;
        var entries = Enumerable.Range(0, 101).Select(index => new RedisEntry(Encoding.UTF8.GetBytes("field" + index),
            index == 0 ? new byte[] { 0, 255 } : Encoding.UTF8.GetBytes("value" + index))).ToArray();
        var redisSession = ProtocolStub<IRedisSession>.For((name, _) => name switch
        {
            "get_DatabaseCount" => 1,
            nameof(IRedisSession.ReadAsync) => Read(),
            _ => throw new InvalidOperationException(name),
        });
        var prompts = new ApprovalStub();
        using var rig = new McpTestRig([target], redis: ProtocolStub<IRedisSessionFactory>.For((_, _) => Task.FromResult(redisSession)),
            defaultAccess: true, approval: prompts);
        await rig.Access.SetExclusionsAsync("", [new("", target.Id, target.TypeId, "0", "secret:")], McpTestRig.Token);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        string id = McpDefaultAccess.Id(session, target);
        var first = Result(await Call(rig, session, McpToolCatalog.RedisReadValue, ("grant_id", id), ("database", "0"), ("key", "order:1")));
        Assert.Equal(100, first.GetProperty("entries").GetArrayLength());
        var binary = first.GetProperty("entries")[0].GetProperty("value");
        Assert.Equal("base64", binary.GetProperty("encoding").GetString());
        Assert.Equal("AP8=", binary.GetProperty("data").GetString());
        string cursor = first.GetProperty("next_cursor").GetString()!;
        var second = Result(await Call(rig, session, McpToolCatalog.RedisReadValue, ("grant_id", id), ("database", "0"),
            ("key", "order:1"), ("cursor", cursor)));
        Assert.Single(second.GetProperty("entries").EnumerateArray());
        Assert.Equal(1, reads);
        Assert.Single(prompts.Requests);
        Result(await Call(rig, session, McpToolCatalog.RedisReadValue, ("grant_id", id), ("database", "0"), ("key", "order:10")));
        Assert.Single(prompts.Requests);
        Assert.True((await Call(rig, session, McpToolCatalog.RedisReadValue, ("grant_id", id), ("database", "0"), ("key", "secret:1"))).IsError);

        Task<RedisSnapshot> Read()
        {
            reads++;
            return Task.FromResult(new RedisSnapshot("hash", entries.Length, -1, entries, true));
        }
    }

    [Fact]
    public void ExclusionsCoverResourcesAndRecursiveAncestorsWithoutBlockingOtherSiblings()
    {
        var postgres = new McpTarget("pg", "Database", "postgresql", "v1");
        var redis = new McpTarget("redis", "Redis", "redis", "v1");
        var policy = new McpLocalPolicy("", "", true, "", [], [new("1", Storage.Id, Storage.TypeId, "bucket", "private/"),
            new("2", postgres.Id, postgres.TypeId, "db", "public", "secret"), new("3", redis.Id, redis.TypeId, "0", "token:")]);
        Assert.True(McpExclusionRules.Blocks(policy, Storage, "bucket", "private/a"));
        Assert.True(McpExclusionRules.Blocks(policy, Storage, "bucket", "", includeDescendants: true));
        Assert.False(McpExclusionRules.Blocks(policy, Storage, "bucket", "public/"));
        Assert.True(McpExclusionRules.Blocks(policy, postgres, "db", "public", "secret"));
        Assert.False(McpExclusionRules.Blocks(policy, postgres, "db", "public", "orders"));
        Assert.True(McpExclusionRules.Blocks(policy, redis, "0", "token:secret"));
        Assert.False(McpExclusionRules.Blocks(policy, redis, "1", "token:secret"));
        var whole = policy with { Exclusions = [new("4", Storage.Id, Storage.TypeId)] };
        Assert.True(McpExclusionRules.Blocks(whole, Storage with { TypeId = "mysql" }));
    }

    [Fact]
    public void MigratingDefaultPolicyRetainsLegacyGrantsWithoutLeavingThemActiveForOlderVersions()
    {
        var oldGrant = new McpGrant("grant", Storage.Id, Storage.TypeId, Storage.Revision, "exports", "", "", false);
        var store = new MemoryMcpStore(Storage) { Policy = new("", new string('A', 32), true, new string('B', 64), [oldGrant]) };
        using var access = new McpAccessService(store, "Hermod.Mcp.exe");
        var session = Assert.IsType<McpSessionAccess>(access.Connect());
        access.MigrateDefaultPolicy();
        Assert.True(session.Revoked.IsCancellationRequested);
        Assert.Empty(store.Policy!.Grants);
        Assert.Equal(oldGrant, Assert.Single(store.Policy.LegacyGrants!));
        Assert.True(store.Policy.DefaultAllow);
        Assert.Equal(0, JsonSerializer.SerializeToElement(store.Policy).GetProperty("Grants").GetArrayLength());
    }

    [Fact]
    public void PreviousPolicyWithoutNewFieldsDefaultsToAllInstances()
    {
        const string json = "{\"Owner\":\"\",\"Generation\":\"\",\"Enabled\":false,\"Secret\":\"\",\"Grants\":[]}";
        var policy = JsonSerializer.Deserialize<McpLocalPolicy>(json)!;
        Assert.True(policy.DefaultAllow);
        Assert.Null(policy.Exclusions);
    }

    private static Task<CallToolResult> Call(McpTestRig rig, McpSessionAccess session, string name, params (string Key, object Value)[] values) =>
        rig.Tools.InvokeAsync(session, name, values.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value)), rig.Cursors, McpTestRig.Token);
    private static JsonElement Result(CallToolResult result)
    {
        Assert.False(result.IsError == true, string.Join(" ", result.Content.OfType<TextContentBlock>().Select(item => item.Text)));
        return result.StructuredContent!.Value;
    }
    private sealed class ApprovalStub(Func<McpApprovalRequest, CancellationToken, Task<McpApprovalResult>>? callback = null) : IMcpApprovalService
    {
        internal List<McpApprovalRequest> Requests { get; } = [];
        public Task<McpApprovalResult> ConfirmAsync(McpApprovalRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return callback?.Invoke(request, cancellationToken) ?? Task.FromResult(new McpApprovalResult(true));
        }
    }
    private sealed class TemporaryFolder : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("Hermod.Mcp.Default.");
        internal string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(true);
    }
}
