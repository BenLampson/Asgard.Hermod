namespace Asgard.Hermod.UI.Tests;

/// <summary>递归日期扫描只读取元数据，覆盖分页、空目录、范围校验及用户取消。</summary>
public sealed class ObjectStorageDirectoryScanTests
{
    /// <summary>嵌套文件的日期跨页聚合，目录占位日期不影响排序，完整结果包含未加载的直接子项。</summary>
    [Fact]
    public async Task Scan_AggregatesNestedFilesAcrossAllPagesWithoutDownloading()
    {
        var older = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var newer = older.AddDays(2);
        var client = new FakeObjectStorageClient
        {
            Read = (_, _, _, _) => throw new InvalidOperationException("不得下载正文。"),
            Metadata = (_, _, _) => throw new InvalidOperationException("不得逐对象查询。"),
            ListObjects = (_, prefix, token, delimiter, size, _) =>
            {
                Assert.Equal("root/", prefix);
                Assert.Null(delimiter);
                Assert.Equal(1000, size);
                return Task.FromResult(token is null
                    ? new ObjectStoragePage([new("root/", true, LastModified: newer.AddDays(5)),
                        new("root/a/deep/old.txt", false, LastModified: older),
                        new("root/b/file.txt", false, LastModified: older)], "next")
                    : new ObjectStoragePage([new("root/a/deeper/new.txt", false, LastModified: newer),
                        new("root/b/", true, LastModified: newer.AddDays(5)),
                        new("root/empty/sub/", true, LastModified: newer),
                        new("root/direct.txt", false, LastModified: newer)]));
            },
        };
        var service = ObjectStorageTestServices.Create(client).Service;
        var result = await service.ScanDirectoryAsync("instance", new("test-bucket", "cn-hangzhou"), "root/",
            (_, _) => throw new InvalidOperationException("少量文件无需确认。"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(4, result.FilesScanned);
        Assert.Equal(["root/a/", "root/b/", "root/empty/", "root/direct.txt"], result.Entries.Select(entry => entry.Key));
        Assert.Equal(newer, result.Entries[0].LastModified);
        Assert.Equal(older, result.Entries[1].LastModified);
        Assert.Null(result.Entries[2].LastModified);
        Assert.Equal(1, client.DisposeCount);
    }

    /// <summary>超过阈值只确认一次，拒绝不返回部分结果，继续则读取后续全部页。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeScan_RequiresDecisionBeforeReadingFurther(bool continueScan)
    {
        int calls = 0, confirmations = 0;
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) =>
            {
                calls++;
                return Task.FromResult(new ObjectStoragePage(Enumerable.Range(0, 1000)
                    .Select(index => new ObjectStorageEntry($"folder/deep/{calls}-{index}.txt", false)).ToArray(),
                    calls < 12 ? calls.ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
            },
        };
        var service = ObjectStorageTestServices.Create(client).Service;
        var result = await service.ScanDirectoryAsync("instance", new("test-bucket", "cn-hangzhou"), "",
            (state, _) =>
            {
                confirmations++;
                Assert.Equal(11_000, state.FilesScanned);
                Assert.Equal(11, state.PagesRead);
                Assert.Equal(11, calls);
                return Task.FromResult(continueScan);
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, confirmations);
        Assert.Equal(continueScan ? 12 : 11, calls);
        if (continueScan) { Assert.NotNull(result); Assert.Equal(12_000, result.FilesScanned); Assert.Single(result.Entries); }
        else Assert.Null(result);
        Assert.Equal(1, client.DisposeCount);
    }

    /// <summary>循环游标与越界对象不能产生貌似完整的排序。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidResponse_FailsSafely(bool outsidePrefix)
    {
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage(
                [new(outsidePrefix ? "outside/private-token.txt" : "root/file.txt", false)], "repeated")),
        };
        var service = ObjectStorageTestServices.Create(client).Service;
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() => service.ScanDirectoryAsync("instance",
            new("test-bucket", "cn-hangzhou"), "root/", (_, _) => Task.FromResult(true),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("InvalidResponse", error.ErrorCode);
        Assert.DoesNotContain("private-token", error.Message);
        Assert.Equal(1, client.DisposeCount);
    }

    /// <summary>即使服务商忽略取消后迟到返回，也不能发布扫描结果。</summary>
    [Fact]
    public async Task Cancellation_DiscardsLatePage()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult(new ObjectStoragePage([new("file.txt", false)]));
            },
        };
        var service = ObjectStorageTestServices.Create(client).Service;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanDirectoryAsync("instance",
            new("test-bucket", "cn-hangzhou"), "", (_, _) => Task.FromResult(true), cancellationToken: cancellation.Token));
        Assert.Equal(1, client.DisposeCount);
    }
}
