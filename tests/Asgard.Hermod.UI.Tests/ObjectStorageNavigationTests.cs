namespace Asgard.Hermod.UI.Tests;

/// <summary>验证侧栏按需读取、分页和页签关闭，避免容器目录丢失或迟到回写。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageNavigationTests(WpfTestFixture fixture)
{
    /// <summary>打开实例之前不读取远端，首次激活按桶区域恢复当前根目录。</summary>
    [Fact]
    public Task Initialize_ConnectsOnlyWhenActivatedAndShowsBuckets() => fixture.RunAsync(async () =>
    {
        var service = new ObjectStorageTestService();
        await using var page = CreatePage(service);
        using var navigation = new ObjectStorageNavigationViewModel(page, service);
        Assert.Empty(navigation.Rows);
        Assert.Equal(0, service.BucketReads);

        await page.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, service.BucketReads);
        Assert.Equal(1, navigation.BucketCount);
        var root = Assert.Single(navigation.Rows);
        Assert.Equal("assets-dev", root.Name);
        Assert.True(root.IsCurrent);
        Assert.True(root.IsExpanded);
        Assert.Equal("cn-hangzhou", root.Bucket.Region);
    });

    /// <summary>一级查询不统计容器全部文件，后续页仍可显示新的目录。</summary>
    [Fact]
    public Task DirectoryPagination_LoadsMoreWithoutNavigating() => fixture.RunAsync(async () =>
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, prefix, _, token, _) => Task.FromResult(prefix != "" ? new ObjectStoragePage([])
                : token is null ? new ObjectStoragePage([new("docs/", true), new("file.txt", false)], "second")
                : new ObjectStoragePage([new("images/", true)])),
        };
        await using var page = CreatePage(service);
        using var navigation = new ObjectStorageNavigationViewModel(page, service);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Contains(navigation.Rows, row => row.Prefix == "docs/");
        Assert.DoesNotContain(navigation.Rows, row => row.Name == "file.txt");
        var more = Assert.Single(navigation.Rows, row => row.IsMore);
        await more.OpenCommand.ExecuteAsync();
        Assert.Equal("", page.Prefix);
        Assert.Contains(navigation.Rows, row => row.Prefix == "images/");
        Assert.DoesNotContain(navigation.Rows, row => row.IsMore);
        Assert.Contains(service.ListCalls, query => query.Continuation == "second" && query.Search == "");
    });

    /// <summary>面包屑切到下一页的前缀时只补齐祖先，不扫描全桶定位路径。</summary>
    [Fact]
    public Task Navigate_ToUnlistedPrefixAddsAncestorsAndMarksCurrent() => fixture.RunAsync(async () =>
    {
        var service = new ObjectStorageTestService();
        await using var page = CreatePage(service);
        using var navigation = new ObjectStorageNavigationViewModel(page, service);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        await page.NavigateAsync(service.Buckets[0], "releases/v0.9.0/", TestContext.Current.CancellationToken);

        var current = Assert.Single(navigation.Rows, row => row.IsCurrent);
        Assert.Equal("releases/v0.9.0/", current.Prefix);
        Assert.Equal(2, current.Depth);
        Assert.Contains(navigation.Rows, row => row.Prefix == "releases/" && row.IsExpanded);
        Assert.True(service.ListCalls.Count <= 6);
    });

    /// <summary>用户可保留当前路径，独立展开或折叠侧栏树而不重新请求已缓存层级。</summary>
    [Fact]
    public Task CollapseAndExpand_ReusesReadDirectory() => fixture.RunAsync(async () =>
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, prefix, _, _, _) => Task.FromResult(new ObjectStoragePage(
                prefix == "" ? [new("docs/", true)] : [])),
        };
        await using var page = CreatePage(service);
        using var navigation = new ObjectStorageNavigationViewModel(page, service);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var root = navigation.Rows[0];
        int calls = service.ListCalls.Count;
        await root.ToggleCommand.ExecuteAsync();
        Assert.Single(navigation.Rows);
        await root.ToggleCommand.ExecuteAsync();
        Assert.Contains(navigation.Rows, row => row.Prefix == "docs/");
        Assert.Equal(calls, service.ListCalls.Count);
    });

    /// <summary>窗口关闭后忽略远端迟到目录，不让后台更新恢复已回收的视图。</summary>
    [Fact]
    public Task ClosingPage_CancelsPendingDirectoryAndIgnoresLateResponse() => fixture.RunAsync(async () =>
    {
        var pending = new TaskCompletionSource<ObjectStoragePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, prefix, _, _, _) => Task.FromResult(new ObjectStoragePage(
                prefix == "" ? [new("docs/", true)] : [])),
        };
        await using var page = CreatePage(service);
        using var navigation = new ObjectStorageNavigationViewModel(page, service);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var directory = navigation.Rows.Single(row => row.Prefix == "docs/");
        service.ListObjectsHandler = (_, _, _, _, _, _) => pending.Task;
        var opening = directory.ToggleCommand.ExecuteAsync();
        await page.CloseAsync();
        pending.SetResult(new([new("late/", true)]));
        await opening;
        Assert.Empty(navigation.Rows);
        Assert.False(directory.IsLoading);
    });

    /// <summary>完整刷新删除旧节点并加入新目录，搜索结果不会删掉完整目录缓存。</summary>
    [Fact]
    public Task RefreshCurrentDirectory_UpdatesFoldersAndSearchKeepsCachedSiblings() => fixture.RunAsync(async () =>
    {
        var folders = new[] { new ObjectStorageEntry("docs/", true), new ObjectStorageEntry("images/", true) };
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, search, _, _) => Task.FromResult(new ObjectStoragePage(
                search.Length == 0 ? folders : [folders[0]])),
        };
        await using var page = CreatePage(service);
        using var navigation = new ObjectStorageNavigationViewModel(page, service);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Contains(navigation.Rows, row => row.Prefix == "docs/");
        folders = [new("images/", true), new("uploads/", true)];
        await page.RefreshAsync(TestContext.Current.CancellationToken);
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.DoesNotContain(navigation.Rows, row => row.Prefix == "docs/");
        Assert.Contains(navigation.Rows, row => row.Prefix == "uploads/");
        page.SearchText = "images";
        await page.RefreshAsync(TestContext.Current.CancellationToken);
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.Contains(navigation.Rows, row => row.Prefix == "uploads/");
    });

    private static ObjectStorageWorkspaceViewModel CreatePage(ObjectStorageTestService service) => new(
        new InstanceItemViewModel(new("configured-oss", "开发 OSS", "开发 OSS", "cloud", [],
            Endpoint: "阿里云 OSS", TypeId: "storage", ProviderId: "aliyun-oss")), service, new ObjectStorageTestDialogs());
}
