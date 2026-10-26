namespace Asgard.Hermod.UI.Tests;

/// <summary>目录统计只列举元数据，覆盖隐式目录、空目录、分页和取消。</summary>
public sealed class ObjectStorageFolderStatisticsTests
{
    /// <summary>目录占位不计入大小，隐式祖先与显式空目录去重，精确范围跨页累计。</summary>
    [Fact]
    public async Task Statistics_AggregatesAllPagesWithoutReadingFiles()
    {
        var date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        int calls = 0;
        var client = new FakeObjectStorageClient
        {
            Read = (_, _, _, _) => throw new InvalidOperationException("不得下载正文。"),
            Metadata = (_, _, _) => throw new InvalidOperationException("不得逐对象读取。"),
            ListObjects = (_, prefix, token, delimiter, size, _) =>
            {
                calls++;
                Assert.Equal("root/", prefix); Assert.Null(delimiter); Assert.Equal(1000, size);
                return Task.FromResult(token is null
                    ? new ObjectStoragePage([new("root/", true, 42), new("root/a/deep/f.txt", false, 1024, date), new("root/empty/", true, 23)], "next")
                    : new ObjectStoragePage([new("root/a/", true), new("root/a/deep/g.txt", false, 10, date.AddDays(1)), new("root/direct", false, 0)]));
            },
        };
        var result = await ObjectStorageTestServices.Create(client).Service.GetFolderStatisticsAsync("instance", new("bucket", "region"), "root/",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new(3, 3, 1034, date.AddDays(1)), result);
        Assert.Equal(2, calls); Assert.Equal(1, client.DisposeCount);
    }

    /// <summary>根目录和只有自身占位的目录都可返回零统计。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("empty/")]
    public async Task EmptyDirectory_ReturnsZero(string prefix)
    {
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage(prefix.Length == 0 ? [] : [new(prefix, true)])),
        };
        var result = await ObjectStorageTestServices.Create(client).Service.GetFolderStatisticsAsync("instance", new("bucket", "region"), prefix,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new(0, 0, 0), result);
    }

    /// <summary>越界对象、负数大小和循环游标不能得到貌似完整的属性。</summary>
    [Theory]
    [InlineData("outside/file", 1)]
    [InlineData("root/file", -1)]
    [InlineData("root/file", 1)]
    public async Task InvalidPages_FailSafely(string key, long size)
    {
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new(key, false, size)], "repeat")),
        };
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() => ObjectStorageTestServices.Create(client).Service
            .GetFolderStatisticsAsync("instance", new("bucket", "region"), "root/", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("InvalidResponse", error.ErrorCode);
        Assert.Equal(1, client.DisposeCount);
    }

    /// <summary>服务商忽略取消时仍丢弃迟到的页面并释放客户端。</summary>
    [Fact]
    public async Task Cancellation_DiscardsLatePage()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) =>
            {
                stop.Cancel();
                return Task.FromResult(new ObjectStoragePage([new("root/file", false, 4)]));
            },
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ObjectStorageTestServices.Create(client).Service
            .GetFolderStatisticsAsync("instance", new("bucket", "region"), "root/", cancellationToken: stop.Token));
        Assert.Equal(1, client.DisposeCount);
    }
}
