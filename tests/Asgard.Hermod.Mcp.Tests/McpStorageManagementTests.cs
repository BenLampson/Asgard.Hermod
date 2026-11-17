using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using Asgard.Hermod.ObjectStorage.Abstractions.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpStorageManagementTests
{
    private static readonly McpTarget Target = new("storage", "Storage", "storage", "v1");

    [Fact]
    public async Task SdkClientDiscoversAndRunsTheDeletionWorkflow()
    {
        List<string[]> deleted = [];
        var storage = ProtocolStub<IObjectStorageClient>.For((name, args) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.DeleteAsync), name);
            deleted.Add(((IReadOnlyList<string>)args[1]!).ToArray());
            return Task.CompletedTask;
        });
        using var rig = Rig(storage);
        var grant = await rig.GrantAsync(Target, "exports", "safe/", delete: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string pipeName = "Hermod.Mcp.Tests." + Guid.NewGuid().ToString("N");
        await using var server = new McpDesktopServer(rig.Access, new(rig.Tools), name: pipeName);
        server.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(McpTestRig.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        await using var pipe = await McpPipeEndpoint.ConnectAsync(pipeName, token);
        Assert.True(await McpPipeEndpoint.AuthenticateAsync(pipe, rig.Session.Policy.Secret, token));
        await using var client = await McpClient.CreateAsync(new StreamClientTransport(pipe, pipe), cancellationToken: token);
        var tools = await client.ListToolsAsync(cancellationToken: token);
        Assert.True(Assert.Single(tools, tool => tool.Name == McpToolCatalog.StorageExecuteDelete).ProtocolTool.Annotations!.DestructiveHint);
        var prepared = await client.CallToolAsync(McpToolCatalog.StoragePrepareDelete, new Dictionary<string, object?>
        {
            ["grant_id"] = grant.Id,
            ["keys"] = new[] { "safe/report.csv" },
        }, cancellationToken: token);
        Assert.NotEqual(true, prepared.IsError);
        string id = prepared.StructuredContent!.Value.GetProperty("plan_id").GetString()!;
        Assert.Empty(deleted);
        var result = await client.CallToolAsync(McpToolCatalog.StorageExecuteDelete, new Dictionary<string, object?>
        {
            ["grant_id"] = grant.Id,
            ["plan_id"] = id,
        }, cancellationToken: token);
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(new[] { "safe/report.csv" }, Assert.Single(deleted));
    }

    [Fact]
    public void LegacyGrantDefaultsNewPermissionsToOff()
    {
        var grant = System.Text.Json.JsonSerializer.Deserialize<McpGrant>("""
            {"Id":"old","InstanceId":"storage","TypeId":"storage","Revision":"v1","Resource":"exports","Qualifier":"safe/","Table":"","ShareContent":false}
            """);
        Assert.NotNull(grant);
        Assert.False(grant.AllowCreateFolder || grant.AllowDelete || grant.AllowSignedLinks || grant.AllowOverwrite);
    }

    [Fact]
    public void DeletionPlanExpiresAfterFiveMinutes()
    {
        long now = 0;
        var cache = new McpCursorCache(() => now);
        string id = cache.Store(McpToolCatalog.StoragePrepareDelete, "grant", "deletion", new[] { "safe/a" });
        now = (long)TimeSpan.FromMinutes(5).TotalMilliseconds + 1;
        Assert.Throws<InvalidOperationException>(() => cache.Take<string[]>(id, McpToolCatalog.StoragePrepareDelete, "grant", "deletion"));
    }

    [Fact]
    public async Task ManagementToolsRequireIndependentOptInAndAdvertiseDestructiveExecution()
    {
        using var rig = new McpTestRig([Target]);
        var readOnly = await rig.GrantAsync(Target, "exports", "safe/");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string[] management = [McpToolCatalog.StorageCreateFolder, McpToolCatalog.StoragePrepareDelete,
            McpToolCatalog.StorageDeletePlan, McpToolCatalog.StorageExecuteDelete, McpToolCatalog.StorageSignedLink];
        Assert.DoesNotContain(rig.Tools.List(rig.Session), tool => management.Contains(tool.Name));
        Assert.True((await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, readOnly, ("keys", new[] { "safe/a" }))).IsError);
        Assert.Equal(0, rig.Store.Opened);

        var allowed = await rig.GrantAsync(Target, "exports", "safe/", delete: true);
        var tools = rig.Tools.List(rig.Session);
        Assert.Contains(tools, tool => tool.Name == McpToolCatalog.StoragePrepareDelete);
        Assert.DoesNotContain(tools, tool => tool.Name is McpToolCatalog.StorageCreateFolder or McpToolCatalog.StorageSignedLink);
        var execute = Assert.Single(tools, tool => tool.Name == McpToolCatalog.StorageExecuteDelete);
        Assert.True(execute.Annotations!.DestructiveHint);
        Assert.False(execute.Annotations.ReadOnlyHint);
        var schema = Assert.Single(tools, tool => tool.Name == McpToolCatalog.StoragePrepareDelete).InputSchema;
        Assert.Equal("array", schema.GetProperty("properties").GetProperty("keys").GetProperty("type").GetString());
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageCreateFolder, allowed, ("key", "safe/new/"))).IsError);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageSignedLink, allowed, ("key", "safe/a"), ("lifetime_minutes", 15))).IsError);
    }

    [Fact]
    public async Task PrepareFreezesDeduplicatedKeysAcrossPagesAndExecuteConsumesPlanOnce()
    {
        List<string[]> deleted = [];
        int listings = 0;
        var client = ProtocolStub<IObjectStorageClient>.For((name, args) =>
        {
            Assert.Equal("exports", args[0]);
            if (name == nameof(IObjectStorageClient.ListObjectsAsync))
            {
                Assert.Equal("safe/dir/", args[1]);
                Assert.Null(args[3]);
                listings++;
                return args[2] is null
                    ? Task.FromResult(new ObjectStoragePage([new("safe/dir/a", false), new("safe/dir/b", false)], "next"))
                    : Task.FromResult(new ObjectStoragePage([new("safe/dir/c", false)]));
            }
            Assert.Equal(nameof(IObjectStorageClient.DeleteAsync), name);
            deleted.Add(((IReadOnlyList<string>)args[1]!).ToArray());
            return Task.CompletedTask;
        });
        using var rig = Rig(client);
        var grant = await rig.GrantAsync(Target, "exports", "safe/", delete: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);

        var prepared = await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, grant,
            ("keys", new[] { "safe/dir/a", "safe/other" }), ("prefixes", new[] { "safe/dir/" }));
        Assert.NotEqual(true, prepared.IsError);
        var plan = prepared.StructuredContent!.Value;
        Assert.Equal(5, plan.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "safe/dir/", "safe/dir/a", "safe/dir/b", "safe/dir/c", "safe/other" },
            plan.GetProperty("keys").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Empty(deleted);
        string id = plan.GetProperty("plan_id").GetString()!;
        var result = await rig.CallAsync(McpToolCatalog.StorageExecuteDelete, grant, ("plan_id", id));
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(5, result.StructuredContent!.Value.GetProperty("submitted_count").GetInt32());
        Assert.Equal(new[] { "safe/dir/", "safe/dir/a", "safe/dir/b", "safe/dir/c", "safe/other" }, Assert.Single(deleted));
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageExecuteDelete, grant, ("plan_id", id))).IsError);
        Assert.Single(deleted);
        Assert.Equal(2, listings);
    }

    [Fact]
    public async Task PlanPagesAreBoundToGrantAndRevocationDisablesExecution()
    {
        var client = ProtocolStub<IObjectStorageClient>.For();
        using var rig = Rig(client);
        var grant = await rig.GrantAsync(Target, "exports", "safe/", delete: true);
        var other = await rig.GrantAsync(Target, "exports", "safe/other/", delete: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string[] keys = Enumerable.Range(0, 31).Select(index => $"safe/{index:D2}").ToArray();
        var plan = (await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, grant, ("keys", keys))).StructuredContent!.Value;
        string id = plan.GetProperty("plan_id").GetString()!;
        Assert.Equal(25, plan.GetProperty("keys").GetArrayLength());
        Assert.Equal(25, plan.GetProperty("next_offset").GetInt32());
        var page = (await rig.CallAsync(McpToolCatalog.StorageDeletePlan, grant, ("plan_id", id), ("offset", 25))).StructuredContent!.Value;
        Assert.Equal(6, page.GetProperty("keys").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, page.GetProperty("next_offset").ValueKind);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageDeletePlan, other, ("plan_id", id))).IsError);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageExecuteDelete, other, ("plan_id", id))).IsError);
        await rig.Access.RemoveGrantAsync("", grant.Id, McpTestRig.Token);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageExecuteDelete, grant, ("plan_id", id))).IsError);
    }

    [Theory]
    [InlineData("unsafe/a")]
    [InlineData("safe/../unsafe/a")]
    [InlineData("safe/%2e%2e/unsafe/a")]
    public async Task DeletionRejectsKeysOutsideTheGrant(string key)
    {
        using var rig = Rig(ProtocolStub<IObjectStorageClient>.For());
        var grant = await rig.GrantAsync(Target, "exports", "safe/", delete: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.True((await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, grant, ("keys", new[] { key }))).IsError);
        Assert.True((await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, grant, ("prefixes", new[] { key }))).IsError);
    }

    [Fact]
    public async Task DeletionRejectsInvalidAndExcessiveListingsBeforeAnyDelete()
    {
        int calls = 0;
        var client = ProtocolStub<IObjectStorageClient>.For((name, _) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.ListObjectsAsync), name);
            return ++calls switch
            {
                1 => Task.FromResult(new ObjectStoragePage([new("safe/another/file", false)])),
                2 => Task.FromResult(new ObjectStoragePage(Enumerable.Range(0, 1001).Select(i => new ObjectStorageEntry($"safe/dir/{i}", false)).ToArray())),
                _ => Task.FromResult(new ObjectStoragePage([], "repeated")),
            };
        });
        using var rig = Rig(client);
        var grant = await rig.GrantAsync(Target, "exports", "safe/", delete: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        for (int i = 0; i < 3; i++)
            Assert.True((await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, grant, ("prefixes", new[] { "safe/dir/" }))).IsError);
        Assert.Equal(4, calls);
        Assert.True((await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, grant, ("keys", "not an array"))).IsError);
        Assert.True((await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, grant, ("prefixes", new[] { "safe/dir" }))).IsError);
    }

    [Fact]
    public async Task FailedDeleteReportsUnknownOutcomeAndCannotReusePlan()
    {
        int writes = 0;
        var client = ProtocolStub<IObjectStorageClient>.For((name, _) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.DeleteAsync), name);
            writes++;
            throw new IOException("synthetic secret");
        });
        using var rig = Rig(client);
        var grant = await rig.GrantAsync(Target, "exports", "safe/", delete: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var plan = (await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, grant, ("keys", new[] { "safe/a" }))).StructuredContent!.Value;
        string id = plan.GetProperty("plan_id").GetString()!;
        var result = await rig.CallAsync(McpToolCatalog.StorageExecuteDelete, grant, ("plan_id", id));
        Assert.True(result.IsError);
        string text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain("synthetic secret", text);
        Assert.Equal(Asgard.Hermod.Abstractions.Localization.Text.Get("McpStorageDeleteOutcomeUnknown"), text);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageExecuteDelete, grant, ("plan_id", id))).IsError);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task ConcurrentExecutionCannotSubmitTheSamePlanTwice()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes = 0;
        var client = ProtocolStub<IObjectStorageClient>.For((name, _) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.DeleteAsync), name);
            Interlocked.Increment(ref writes);
            started.TrySetResult();
            return finish.Task;
        });
        using var rig = Rig(client);
        var grant = await rig.GrantAsync(Target, "exports", "safe/", delete: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var plan = (await rig.CallAsync(McpToolCatalog.StoragePrepareDelete, grant, ("keys", new[] { "safe/a" }))).StructuredContent!.Value;
        string id = plan.GetProperty("plan_id").GetString()!;
        Task<CallToolResult> first = rig.CallAsync(McpToolCatalog.StorageExecuteDelete, grant, ("plan_id", id));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), McpTestRig.Token);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageExecuteDelete, grant, ("plan_id", id))).IsError);
        finish.SetResult();
        Assert.NotEqual(true, (await first).IsError);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task FolderCreationAndSignedLinkStayInScopeAndUseExplicitPermissions()
    {
        int uploads = 0, links = 0;
        var client = ProtocolStub<IObjectStorageClient>.For((name, args) => name switch
        {
            nameof(IObjectStorageClient.ObjectExistsAsync) => Task.FromResult(false),
            nameof(IObjectStorageClient.ListObjectsAsync) => Task.FromResult(new ObjectStoragePage([])),
            nameof(IObjectStorageClient.UploadAsync) => Upload(args),
            nameof(IObjectStorageClient.CreateTemporaryLink) => Link(args),
            _ => throw new InvalidOperationException(name),
        });
        Task Upload(object?[] args)
        {
            uploads++;
            Assert.Equal("safe/new/", args[1]);
            Assert.Equal(0L, args[3]);
            Assert.Equal(false, args[4]);
            return Task.CompletedTask;
        }
        string Link(object?[] args)
        {
            links++;
            Assert.Equal("safe/item", args[1]);
            Assert.Equal(TimeSpan.FromMinutes(15), args[2]);
            return "https://example.invalid/temporary";
        }
        using var rig = Rig(client);
        var grant = await rig.GrantAsync(Target, "exports", "safe/", createFolder: true, signedLinks: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.NotEqual(true, (await rig.CallAsync(McpToolCatalog.StorageCreateFolder, grant, ("key", "safe/new/"))).IsError);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageCreateFolder, grant, ("key", "safe/../secret/"))).IsError);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageCreateFolder, grant, ("key", "safe/new"))).IsError);
        var result = await rig.CallAsync(McpToolCatalog.StorageSignedLink, grant, ("key", "safe/item"), ("lifetime_minutes", 15));
        Assert.Equal("https://example.invalid/temporary", result.StructuredContent!.Value.GetProperty("url").GetString());
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageSignedLink, grant, ("key", "other/item"), ("lifetime_minutes", 15))).IsError);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageSignedLink, grant, ("key", "safe/item"), ("lifetime_minutes", 16))).IsError);
        Assert.Equal(1, uploads);
        Assert.Equal(1, links);
    }

    [Fact]
    public async Task FolderStatisticsCountNestedFilesAndFoldersAcrossPages()
    {
        int calls = 0;
        var modified = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var client = ProtocolStub<IObjectStorageClient>.For((name, args) =>
        {
            Assert.Equal(nameof(IObjectStorageClient.ListObjectsAsync), name);
            Assert.Equal("safe/", args[1]);
            Assert.Null(args[3]);
            calls++;
            return args[2] is null
                ? Task.FromResult(new ObjectStoragePage([new("safe/", true), new("safe/a.txt", false, 10, modified),
                    new("safe/sub/", true)], "next"))
                : Task.FromResult(new ObjectStoragePage([new("safe/sub/b.txt", false, 15, modified.AddDays(1)),
                    new("safe/sub/nested/c.txt", false, 5, modified)]));
        });
        using var rig = Rig(client);
        var grant = await rig.GrantAsync(Target, "exports", "safe/");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var result = (await rig.CallAsync(McpToolCatalog.StorageStatistics, grant)).StructuredContent!.Value;
        Assert.Equal(3, result.GetProperty("files").GetInt64());
        Assert.Equal(2, result.GetProperty("folders").GetInt32());
        Assert.Equal(30, result.GetProperty("bytes").GetInt64());
        Assert.Equal(modified.AddDays(1), result.GetProperty("last_modified").GetDateTimeOffset());
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageStatistics, grant, ("prefix", "other/"))).IsError);
        Assert.True((await rig.CallAsync(McpToolCatalog.StorageStatistics, grant, ("prefix", "safe/not-a-folder"))).IsError);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FolderStatisticsRejectOutOfRangeProviderResults()
    {
        var client = ProtocolStub<IObjectStorageClient>.For((_, _) =>
            Task.FromResult(new ObjectStoragePage([new("private/secret", false, 12)])));
        using var rig = Rig(client);
        var grant = await rig.GrantAsync(Target, "exports", "safe/");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var result = await rig.CallAsync(McpToolCatalog.StorageStatistics, grant);
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
    }

    [Fact]
    public async Task FolderCreationReportsConditionalConflictAsExisting()
    {
        var client = ProtocolStub<IObjectStorageClient>.For((name, _) => name switch
        {
            nameof(IObjectStorageClient.ObjectExistsAsync) => Task.FromResult(false),
            nameof(IObjectStorageClient.ListObjectsAsync) => Task.FromResult(new ObjectStoragePage([])),
            nameof(IObjectStorageClient.UploadAsync) => throw Asgard.Hermod.ObjectStorage.Abstractions.Services.ObjectStorageException
                .FromResource("McpStorageFolderExists", "FileAlreadyExists"),
            _ => throw new InvalidOperationException(name),
        });
        using var rig = Rig(client);
        var grant = await rig.GrantAsync(Target, "exports", "safe/", createFolder: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var result = await rig.CallAsync(McpToolCatalog.StorageCreateFolder, grant, ("key", "safe/existing/"));
        Assert.True(result.IsError);
        Assert.Equal(Asgard.Hermod.Abstractions.Localization.Text.Get("McpStorageFolderExists"),
            Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    private static McpTestRig Rig(IObjectStorageClient client) =>
        new([Target], storage: ProtocolStub<IObjectStorageClientFactory>.For((_, _) => client));
}
