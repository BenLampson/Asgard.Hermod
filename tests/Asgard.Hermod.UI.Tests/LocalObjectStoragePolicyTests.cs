using Asgard.Hermod.UI.Services;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证实例身份路由、完整目录确认和操作范围策略，不依赖云网络。</summary>
public sealed class LocalObjectStoragePolicyTests
{
    /// <summary>服务按实例标识恢复参数，按当前目录分页并匹配名称。</summary>
    [Fact]
    public async Task ListObjectsAsync_UsesSavedIdentityRegionPrefixAndPageSize()
    {
        var client = new FakeObjectStorageClient
        {
            ListObjects = (bucket, prefix, token, delimiter, size, _) =>
            {
                Assert.Equal("test-bucket", bucket);
                Assert.Equal("docs/", prefix);
                Assert.Equal("next-page", token);
                Assert.Equal("/", delimiter);
                Assert.Equal(100, size);
                return Task.FromResult(new ObjectStoragePage([new("docs/readme.md", false)], "another-page"));
            },
        };
        var context = ObjectStorageTestServices.Create(client);

        ObjectStoragePage page = await context.Service.ListObjectsAsync("saved-instance", new("test-bucket", "cn-beijing"),
            "docs/", "read", "next-page", TestContext.Current.CancellationToken);

        Assert.Single(page.Entries);
        Assert.Equal("another-page", page.NextToken);
        Assert.Equal(["saved-instance"], context.Configuration.LoadedInstanceIds);
        Assert.NotNull(context.Provider.ReceivedOptions);
        Assert.Equal("private-resource-token", context.Provider.ReceivedOptions.Parameters["apiToken"]);
        Assert.Equal("cn-beijing", context.Provider.ReceivedRegion);
        Assert.Equal(1, client.DisposeCount);
    }

    /// <summary>目录枚举包含所有分页和占位对象，执行阶段不再增加新增对象到删除范围。</summary>
    [Fact]
    public async Task Deletion_UsesCompleteConfirmedKeysWithoutReenumerating()
    {
        int listCalls = 0;
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, prefix, token, delimiter, size, _) =>
            {
                listCalls++;
                Assert.Equal("docs/", prefix);
                Assert.Null(delimiter);
                Assert.Equal(100, size);
                return Task.FromResult(token is null
                    ? new ObjectStoragePage([new("docs/", true), new("docs/a.txt", false)], "second")
                    : new ObjectStoragePage([new("docs/sub/", true), new("docs/sub/b.txt", false)]));
            },
        };
        var context = ObjectStorageTestServices.Create(client);
        ObjectStorageBucket bucket = new("test-bucket", "cn-hangzhou");

        ObjectStorageDeletePlan plan = await context.Service.PrepareDeletionAsync("saved-instance", bucket,
            [new("docs/", true), new("outside.txt", false), new("docs/a.txt", false)], TestContext.Current.CancellationToken);

        Assert.Equal(2, listCalls);
        Assert.Equal(["docs/", "docs/a.txt", "docs/sub/", "docs/sub/b.txt", "outside.txt"], plan.Keys);
        client.ListObjects = (_, _, _, _, _, _) => throw new InvalidOperationException("执行阶段不得再次枚举目录。");
        client.Delete = (_, keys, _) =>
        {
            Assert.Equal(plan.Keys, keys);
            Assert.DoesNotContain("docs/new-after-confirmation.txt", keys);
            return Task.CompletedTask;
        };
        await context.Service.DeleteAsync("saved-instance", bucket, plan, TestContext.Current.CancellationToken);
        Assert.Equal(2, listCalls);
    }

    /// <summary>不可信范围和重复游标不能生成供 UI 确认的删除清单。</summary>
    [Theory]
    [InlineData(false, "InvalidDeleteRange")]
    [InlineData(true, "InvalidResponse")]
    public async Task PrepareDeletionAsync_RejectsInvalidScopeOrRepeatedCursor(bool repeat, string expectedCode)
    {
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) => Task.FromResult(repeat
                ? new ObjectStoragePage([new("docs/a.txt", false)], "same-cursor")
                : new ObjectStoragePage([new("docs-other/outside.txt", false)])),
            Delete = (_, _, _) => throw new InvalidOperationException("准备阶段不能写远程数据。"),
        };
        var context = ObjectStorageTestServices.Create(client);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => context.Service.PrepareDeletionAsync(
            "saved-instance", new("test-bucket", "cn-hangzhou"), [new("docs/", true)], TestContext.Current.CancellationToken));

        Assert.Equal(expectedCode, error.ErrorCode);
    }

    /// <summary>批量删除只发送明确键，后续批次失败必须告知已有部分对象被删除。</summary>
    [Fact]
    public async Task DeleteAsync_ReportsPartialFailureAfterCompletedBatch()
    {
        var batches = new List<string[]>();
        var client = new FakeObjectStorageClient
        {
            Delete = (_, keys, _) =>
            {
                batches.Add(keys.ToArray());
                if (batches.Count == 2) throw new ObjectStorageException("权限不足。", "AccessDenied");
                return Task.CompletedTask;
            },
        };
        var context = ObjectStorageTestServices.Create(client);
        string[] keys = Enumerable.Range(0, 1001).Select(index => $"docs/file-{index}.txt").ToArray();

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => context.Service.DeleteAsync(
            "saved-instance", new("test-bucket", "cn-hangzhou"), new(keys), TestContext.Current.CancellationToken));

        Assert.Equal("PartialDelete", error.ErrorCode);
        Assert.Equal([1000, 1], batches.Select(batch => batch.Length));
        Assert.Equal(keys, batches.SelectMany(batch => batch));
    }

    /// <summary>新建目录检测同名普通对象，并将未授权覆盖标志交给服务端写入。</summary>
    [Fact]
    public async Task CreateFolderAsync_RejectsExistingFileAndForbidsOverwrite()
    {
        int uploads = 0;
        var client = new FakeObjectStorageClient
        {
            Exists = (_, key, _) => Task.FromResult(key == "docs/existing"),
            Upload = (_, key, body, size, overwrite, _, _) =>
            {
                uploads++;
                Assert.Equal("docs/new-folder/", key);
                Assert.Equal(0, size);
                Assert.True(body.CanRead);
                Assert.False(overwrite);
                return Task.CompletedTask;
            },
        };
        var context = ObjectStorageTestServices.Create(client);
        ObjectStorageBucket bucket = new("test-bucket", "cn-hangzhou");
        ObjectStorageException conflict = await Assert.ThrowsAsync<ObjectStorageException>(() => context.Service.CreateFolderAsync(
            "saved-instance", bucket, "docs/", "existing", TestContext.Current.CancellationToken));
        Assert.Equal("FileAlreadyExists", conflict.ErrorCode);
        await context.Service.CreateFolderAsync("saved-instance", bucket, "docs/", "new-folder", TestContext.Current.CancellationToken);
        Assert.Equal(1, uploads);
    }

    /// <summary>只允许 UI 提供的两个固定短生命周期。</summary>
    [Theory]
    [InlineData(15, true)]
    [InlineData(60, true)]
    [InlineData(30, false)]
    public async Task CreateTemporaryLinkAsync_EnforcesLimitedLifetime(int minutes, bool allowed)
    {
        var client = new FakeObjectStorageClient
        {
            Link = (_, key, lifetime) =>
            {
                Assert.Equal("docs/readme.md", key);
                Assert.Equal(TimeSpan.FromMinutes(minutes), lifetime);
                return "https://storage.example.test/temporary";
            },
        };
        var context = ObjectStorageTestServices.Create(client);
        Task<string> operation = context.Service.CreateTemporaryLinkAsync("saved-instance", new("test-bucket", "cn-hangzhou"),
            "docs/readme.md", TimeSpan.FromMinutes(minutes), TestContext.Current.CancellationToken);
        if (allowed) Assert.Equal("https://storage.example.test/temporary", await operation);
        else Assert.Equal("InvalidLinkLifetime", (await Assert.ThrowsAsync<ObjectStorageException>(() => operation)).ErrorCode);
    }

    /// <summary>不同实例类型的同名连接器和不存在实例不能获得原类型的凭据。</summary>
    [Fact]
    public async Task ResourceFactory_RejectsMissingOrMismatchedSavedType()
    {
        var client = new FakeObjectStorageClient();
        var storage = new FakeObjectStorageProvider("storage", "shared", client);
        var factory = new ObjectStorageClientFactory([storage]);
        var configuration = new ObjectStorageTestConfigurationService(new InstanceConnectionOptions
        {
            TypeId = "docker",
            ProviderId = "shared",
            Parameters = new Dictionary<string, string> { ["engineToken"] = "private" },
        });
        var service = new LocalObjectStorageService(configuration, factory);
        ObjectStorageException mismatch = await Assert.ThrowsAsync<ObjectStorageException>(() => service.ListBucketsAsync(
            "docker-instance", TestContext.Current.CancellationToken));
        Assert.Equal("UnsupportedProvider", mismatch.ErrorCode);
        Assert.Null(storage.ReceivedOptions);
        var missing = new LocalObjectStorageService(new ObjectStorageTestConfigurationService(null), factory);
        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => missing.ListBucketsAsync(
            "missing-instance", TestContext.Current.CancellationToken));
        Assert.Equal("InstanceNotFound", error.ErrorCode);
        Assert.Null(storage.ReceivedOptions);
    }
}
