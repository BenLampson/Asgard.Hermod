using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证惰性读取、分页、目录取消和错误恢复，不访问真实服务。</summary>
public sealed class ObjectStorageWorkspaceNavigationTests
{
    /// <summary>重复激活只读取一次，查询保持当前前缀，下一批去重并继续使用原游标。</summary>
    [Fact]
    public async Task InitializeAndSearch_UseCurrentPrefixAndContinuation()
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, prefix, search, continuation, _) => Task.FromResult(search.Length == 0
                ? new ObjectStoragePage([new(prefix + "docs/", true), new(prefix + "other.txt", false)])
                : continuation is null ? new([new(prefix + "release.json", false)], "next")
                : new([new(prefix + "release.json", false), new(prefix + "release.txt", false)]))
        };
        await using var model = Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, service.BucketReads);
        var bucket = Assert.Single(model.Buckets);
        await model.NavigateAsync(bucket, "releases/v1", TestContext.Current.CancellationToken);
        model.Entries[0].IsChecked = true;
        await model.SelectEntryAsync(model.Entries[1], TestContext.Current.CancellationToken);

        await model.SearchAsync("release", TestContext.Current.CancellationToken);
        Assert.Equal("releases/v1/", model.Prefix);
        Assert.Equal(0, model.CheckedCount);
        Assert.Null(model.SelectedEntry);
        Assert.True(model.LoadMoreCommand.CanExecute(null));
        await model.LoadMoreCommand.ExecuteAsync();

        Assert.Equal(2, model.Entries.Count);
        Assert.False(model.LoadMoreCommand.CanExecute(null));
        Assert.Equal((bucket.Name, "releases/v1/", "release", "next"), service.ListCalls.Last());
        Assert.Equal(3, model.Breadcrumbs.Count);
        Assert.Equal("releases/v1/", model.Breadcrumbs.Last().Prefix);
    }

    /// <summary>迟到的目录响应即使忽略取消，也不能覆盖新目录或结束新请求状态。</summary>
    [Fact]
    public async Task Navigate_IgnoresCancelledLateDirectory()
    {
        var delayed = new TaskCompletionSource<ObjectStoragePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, prefix, _, _, token) =>
            {
                if (prefix != "old/") return Task.FromResult(new ObjectStoragePage([new(prefix + "current.json", false)]));
                oldToken = token;
                return delayed.Task;
            }
        };
        await using var model = Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.Entries[0].IsChecked = true;
        var old = model.NavigateAsync(model.Buckets[0], "old/", TestContext.Current.CancellationToken);
        Assert.True(model.IsLoading);
        Assert.Equal(0, model.CheckedCount);
        await model.NavigateAsync(model.Buckets[0], "new/", TestContext.Current.CancellationToken);
        Assert.True(oldToken.IsCancellationRequested);
        delayed.SetResult(new([new("old/late.json", false)]));
        await old;

        Assert.Equal("new/", model.Prefix);
        Assert.Equal("new/current.json", Assert.Single(model.Entries).Key);
        Assert.False(model.IsLoading);
    }

    /// <summary>先失败后重试仍保留恢复入口，未知错误不会泄漏原始消息。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BucketReadFailure_IsSafeAndCanRetry(bool safe)
    {
        var service = new ObjectStorageTestService();
        service.ListBucketsHandler = (_, _) =>
        {
            if (safe) throw ObjectStorageException.FromResource("InsufficientPermissionsCheckTheInstanceCredentialsAndAccessPolicy", "AccessDenied");
            throw new InvalidOperationException("secret-key=do-not-show");
        };
        await using var model = Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(model.IsInitialized);
        Assert.True(model.ShowRecovery);
        Assert.DoesNotContain("secret-key", model.ReadError, StringComparison.Ordinal);
        if (safe) Assert.Contains("AccessDenied", model.ReadError, StringComparison.Ordinal);

        service.ListBucketsHandler = null;
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(model.IsInitialized);
        Assert.Empty(model.ReadError);
        Assert.Equal("已连接", model.Status);
        Assert.True(model.ShowEmptyActions);
    }

    /// <summary>没有可访问的容器与空目录区分，不能把它误报为已读取空目录。</summary>
    [Fact]
    public async Task NoBuckets_ShowsRecoveryAndDisablesWrites()
    {
        var service = new ObjectStorageTestService { Buckets = [] };
        await using var model = Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal("没有可访问的 Bucket", model.StateTitle);
        Assert.True(model.ShowRecovery);
        Assert.False(model.ShowEmptyActions);
        Assert.False(model.UploadCommand.CanExecute(null));
        Assert.Empty(service.ListCalls);
    }

    /// <summary>手动刷新容器即使名称相同也通知宿主失效目录缓存。</summary>
    [Fact]
    public async Task RefreshBuckets_RereadsAndAlwaysInvalidatesHostCache()
    {
        var service = new ObjectStorageTestService();
        await using var model = Create(service);
        int changes = 0;
        model.BucketsChanged += (_, _) => changes++;
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        int before = changes;
        await model.RefreshBucketsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, service.BucketReads);
        Assert.True(changes > before);
        Assert.Equal("assets-dev", model.SelectedBucket?.Name);
    }

    /// <summary>初始化中的容器请求在关闭时取消，迟到结果不重新构造页面导航。</summary>
    [Fact]
    public async Task Close_IgnoresLateBuckets()
    {
        var delayed = new TaskCompletionSource<IReadOnlyList<ObjectStorageBucket>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken readToken = default;
        var service = new ObjectStorageTestService
        {
            ListBucketsHandler = (_, token) => { readToken = token; return delayed.Task; }
        };
        await using var model = Create(service);
        var initialize = model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.CloseAsync();
        delayed.SetResult(service.Buckets);
        await initialize;
        Assert.True(readToken.IsCancellationRequested);
        Assert.Empty(model.Buckets);
        Assert.Empty(service.ListCalls);
        Assert.False(model.IsInitialized);
    }

    /// <summary>统一使用安全保存身份构造对象页。</summary>
    internal static ObjectStorageWorkspaceViewModel Create(ObjectStorageTestService service, ObjectStorageTestDialogs? dialogs = null, IInstanceEditor? editor = null) =>
        new(new InstanceItemViewModel(new InstanceSummary("oss", "oss", "OSS · Development", "cloud", ["Development"],
            Endpoint: "阿里云 OSS", TypeId: "storage", ProviderId: "aliyun-oss")), service, dialogs ?? new(), editor);
}
