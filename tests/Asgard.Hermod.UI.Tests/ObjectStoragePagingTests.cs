using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证重新读取时分页游标失效，失败重试与迟到响应不能混用查询。</summary>
public sealed class ObjectStoragePagingTests
{
    /// <summary>搜索和刷新首批失败都清除旧游标，恢复后只分页读取新结果。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirstPageFailure_InvalidatesOldCursorAndRetryUsesNewCursor(bool search)
    {
        int phase = 0;
        var failedResponse = new TaskCompletionSource<ObjectStoragePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, continuation, _) => phase switch
            {
                0 => Task.FromResult(new ObjectStoragePage([new("original.txt", false)], "old-token")),
                1 => failedResponse.Task,
                _ => Task.FromResult(continuation is null
                    ? new ObjectStoragePage([new("fresh-first.txt", false)], "new-token")
                    : new ObjectStoragePage([new("fresh-second.txt", false)]))
            }
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(model.HasMore);
        Assert.True(model.LoadMoreCommand.CanExecute(null));

        phase = 1;
        var failedRead = search
            ? model.SearchAsync("fresh", TestContext.Current.CancellationToken)
            : model.RefreshAsync(TestContext.Current.CancellationToken);
        bool hadMoreDuringRead = model.HasMore;
        bool couldContinueDuringRead = model.LoadMoreCommand.CanExecute(null);
        failedResponse.SetException(ObjectStorageException.FromResource("InsufficientPermissionsCheckTheInstanceCredentialsAndAccessPolicy", "AccessDenied"));
        await failedRead;

        Assert.False(hadMoreDuringRead);
        Assert.False(couldContinueDuringRead);
        Assert.False(model.IsLoading);
        Assert.False(model.HasMore);
        Assert.False(model.LoadMoreCommand.CanExecute(null));
        Assert.Empty(model.Entries);
        Assert.Contains("AccessDenied", model.ReadError, StringComparison.Ordinal);

        phase = 2;
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Empty(model.ReadError);
        Assert.Equal("fresh-first.txt", Assert.Single(model.Entries).Key);
        Assert.True(model.HasMore);
        Assert.True(model.LoadMoreCommand.CanExecute(null));
        await model.LoadMoreCommand.ExecuteAsync();

        Assert.Equal(new[] { "fresh-first.txt", "fresh-second.txt" }, model.Entries.Select(entry => entry.Key));
        Assert.False(model.HasMore);
        Assert.False(model.LoadMoreCommand.CanExecute(null));
        Assert.Equal(new string?[] { null, null, null, "new-token" }, service.ListCalls.Select(call => call.Continuation));
        string query = search ? "fresh" : "";
        Assert.Equal(new[] { "", query, query, query }, service.ListCalls.Select(call => call.Search));
    }

    /// <summary>新搜索取消旧分页，忽略取消的迟到页不能恢复旧游标或结束新加载状态。</summary>
    [Fact]
    public async Task Search_IgnoresLateCancelledPageAndUsesCurrentCursor()
    {
        var oldResponse = new TaskCompletionSource<ObjectStoragePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentResponse = new TaskCompletionSource<ObjectStoragePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, query, continuation, token) =>
            {
                if (query == "fresh")
                    return continuation is null ? currentResponse.Task
                        : Task.FromResult(new ObjectStoragePage([new("fresh-second.txt", false)]));
                if (continuation is null)
                    return Task.FromResult(new ObjectStoragePage([new("original.txt", false)], "old-token"));
                oldToken = token;
                return oldResponse.Task;
            }
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var oldPage = model.LoadMoreCommand.ExecuteAsync();
        var currentSearch = model.SearchAsync("fresh", TestContext.Current.CancellationToken);
        bool wasCancelled = oldToken.IsCancellationRequested;
        oldResponse.SetResult(new([new("old-late.txt", false)], "stale-next-token"));
        await oldPage;
        bool stillLoadingAfterLatePage = model.IsLoading;
        bool hadMoreAfterLatePage = model.HasMore;
        int rowsAfterLatePage = model.Entries.Count;
        currentResponse.SetResult(new([new("fresh-first.txt", false)], "fresh-token"));
        await currentSearch;

        Assert.True(wasCancelled);
        Assert.True(stillLoadingAfterLatePage);
        Assert.False(hadMoreAfterLatePage);
        Assert.Equal(0, rowsAfterLatePage);
        Assert.True(model.LoadMoreCommand.CanExecute(null));
        await model.LoadMoreCommand.ExecuteAsync();

        Assert.Equal(new[] { "fresh-first.txt", "fresh-second.txt" }, model.Entries.Select(entry => entry.Key));
        Assert.False(model.HasMore);
        Assert.False(model.IsLoading);
        Assert.Equal(new string?[] { null, "old-token", null, "fresh-token" }, service.ListCalls.Select(call => call.Continuation));
        Assert.Equal(new[] { "", "", "fresh", "fresh" }, service.ListCalls.Select(call => call.Search));
    }

    /// <summary>输入搜索词立即清除旧游标，防抖结束前不分页请求新词，完成首批后才使用新游标。</summary>
    [Fact]
    public async Task SearchText_InvalidatesCursorDuringDebounceAndContinuesOnlyNewQuery()
    {
        var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<ObjectStoragePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, query, continuation, token) =>
            {
                if (query != "new")
                    return Task.FromResult(new ObjectStoragePage([new("original.txt", false)], "old-token"));
                if (continuation is not null)
                    return Task.FromResult(new ObjectStoragePage([new("new-second.txt", false)]));
                firstReadStarted.TrySetResult();
                return firstResponse.Task.WaitAsync(token);
            }
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(model.HasMore);
        Assert.True(model.LoadMoreCommand.CanExecute(null));
        model.NavigationChanged += (_, _) =>
        {
            if (model.SearchText == "new") firstReadCompleted.TrySetResult();
        };

        model.SearchText = "new";
        Assert.False(model.HasMore);
        Assert.False(model.LoadMoreCommand.CanExecute(null));
        Assert.Single(service.ListCalls);

        await firstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(model.IsLoading);
        Assert.False(model.HasMore);
        Assert.False(model.LoadMoreCommand.CanExecute(null));
        Assert.Equal(("assets-dev", "", "new", (string?)null), service.ListCalls.Last());
        firstResponse.SetResult(new([new("new-first.txt", false)], "new-token"));
        await firstReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("new-first.txt", Assert.Single(model.Entries).Key);
        Assert.True(model.HasMore);
        Assert.True(model.LoadMoreCommand.CanExecute(null));
        await model.LoadMoreCommand.ExecuteAsync();

        Assert.Equal(new[] { "new-first.txt", "new-second.txt" }, model.Entries.Select(entry => entry.Key));
        Assert.False(model.HasMore);
        Assert.False(model.LoadMoreCommand.CanExecute(null));
        Assert.Equal(new string?[] { null, null, "new-token" }, service.ListCalls.Select(call => call.Continuation));
        Assert.Equal(new[] { "", "new", "new" }, service.ListCalls.Select(call => call.Search));
    }
}
