namespace Asgard.Hermod.UI.Tests;

/// <summary>验证名称包含匹配、分页完整性以及取消和不可信响应边界。</summary>
public sealed class ObjectStorageFuzzySearchTests
{
    /// <summary>普通词匹配名称中间和不同大小写，标点不解释为正则表达式。</summary>
    [Theory]
    [InlineData("READ", "docs/my-readme.md", false)]
    [InlineData("read", "docs/my-READ-folder/", true)]
    [InlineData(".md", "docs/my-readme.md", false)]
    [InlineData("[1]", "docs/file[1].txt", false)]
    [InlineData("文件", "docs/我的文件.txt", false)]
    public async Task Search_MatchesNamesContainingLiteralText(string query, string key, bool directory)
    {
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, prefix, _, delimiter, size, _) =>
            {
                Assert.Equal("docs/", prefix);
                Assert.Equal("/", delimiter);
                Assert.Equal(100, size);
                return Task.FromResult(new ObjectStoragePage([new("docs/", true), new("docs/unrelated.txt", false), new(key, directory)]));
            },
        };
        var context = ObjectStorageTestServices.Create(client);

        var page = await context.Service.ListObjectsAsync("saved-instance", new("test-bucket", "cn-beijing"),
            "docs/", query, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(key, Assert.Single(page.Entries).Key);
    }

    /// <summary>通配符覆盖完整名称，星号可匹配空串，问号恰好匹配一个字符，大小写不敏感。</summary>
    [Theory]
    [InlineData("*.png", "image.png", true)]
    [InlineData("*.png", "IMAGE.PNG", true)]
    [InlineData("*.png", "image.png.bak", false)]
    [InlineData("*.png", "image.jpg", false)]
    [InlineData("image*.png", "image.png", true)]
    [InlineData("image*.png", "image-01.png", true)]
    [InlineData("image?.png", "image1.png", true)]
    [InlineData("image?.png", "image.png", false)]
    [InlineData("image?.png", "image12.png", false)]
    [InlineData("文件?.png", "文件一.png", true)]
    [InlineData("*[1].png", "image[1].png", true)]
    [InlineData("*[1].png", "image1.png", false)]
    [InlineData("*", "image.txt", true)]
    public async Task Search_MatchesWildcardPatterns(string query, string name, bool expected)
    {
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("docs/" + name, false)])),
        };
        var context = ObjectStorageTestServices.Create(client);
        var page = await context.Service.ListObjectsAsync("saved-instance", new("test-bucket", "cn-beijing"),
            "docs/", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(expected ? 1 : 0, page.Entries.Count);
    }

    /// <summary>跨过空匹配页，保留服务商游标；后续搜索仍能找到尚未加载的匹配对象。</summary>
    [Fact]
    public async Task Search_SkipsUnmatchedPagesAndPreservesContinuation()
    {
        var tokens = new List<string?>();
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, token, _, _, _) =>
            {
                tokens.Add(token);
                return Task.FromResult(token switch
                {
                    null => new ObjectStoragePage([new("docs/a.txt", false)], "second"),
                    "second" => new ObjectStoragePage([new("docs/my-REPORT.txt", false)], "third"),
                    "third" => new ObjectStoragePage([new("docs/b.txt", false)], "last"),
                    _ => new ObjectStoragePage([new("docs/final-report.txt", false)]),
                });
            },
        };
        var context = ObjectStorageTestServices.Create(client);
        ObjectStorageBucket bucket = new("test-bucket", "cn-beijing");
        var first = await context.Service.ListObjectsAsync("saved-instance", bucket, "docs/", " report ",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("docs/my-REPORT.txt", Assert.Single(first.Entries).Key);
        Assert.Equal("third", first.NextToken);
        var last = await context.Service.ListObjectsAsync("saved-instance", bucket, "docs/", "report", first.NextToken,
            TestContext.Current.CancellationToken);
        Assert.Equal("docs/final-report.txt", Assert.Single(last.Entries).Key);
        Assert.Null(last.NextToken);
        Assert.Equal(new string?[] { null, "second", "third", "last" }, tokens);
    }

    /// <summary>目录路径不会命中名称；查到末页后明确返回空结果。</summary>
    [Fact]
    public async Task Search_DoesNotMatchParentDirectory()
    {
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("docs/a.txt", false)])),
        };
        var context = ObjectStorageTestServices.Create(client);
        var page = await context.Service.ListObjectsAsync("saved-instance", new("test-bucket", "cn-beijing"),
            "docs/", "docs", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(page.Entries);
        Assert.Null(page.NextToken);
    }

    /// <summary>重复游标不能让稀疏搜索无限枚举，越界对象也不能混入结果。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_RejectsInvalidResponses(bool repeatedCursor)
    {
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) => Task.FromResult(repeatedCursor
                ? new ObjectStoragePage([new("docs/a.txt", false)], "repeat")
                : new ObjectStoragePage([new("outside/report.txt", false)])),
        };
        var context = ObjectStorageTestServices.Create(client);
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() => context.Service.ListObjectsAsync(
            "saved-instance", new("test-bucket", "cn-beijing"), "docs/", "report",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("InvalidResponse", error.ErrorCode);
    }

    /// <summary>枚举取消后不再请求下一页，也不发布迟到的匹配结果。</summary>
    [Fact]
    public async Task Search_StopsWhenCancelled()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int calls = 0;
        var client = new FakeObjectStorageClient
        {
            ListObjects = (_, _, _, _, _, _) =>
            {
                calls++;
                cancellation.Cancel();
                return Task.FromResult(new ObjectStoragePage([new("docs/report.txt", false)], "next"));
            },
        };
        var context = ObjectStorageTestServices.Create(client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Service.ListObjectsAsync(
            "saved-instance", new("test-bucket", "cn-beijing"), "docs/", "report", cancellationToken: cancellation.Token));
        Assert.Equal(1, calls);
        Assert.Equal(1, client.DisposeCount);
    }
}
