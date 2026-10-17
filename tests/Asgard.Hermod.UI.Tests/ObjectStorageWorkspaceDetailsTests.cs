namespace Asgard.Hermod.UI.Tests;

/// <summary>详情预览与对象选择和目录生命周期同步，文本不会交给执行器。</summary>
public sealed class ObjectStorageWorkspaceDetailsTests
{
    /// <summary>选择另一个对象后，旧详情迟到不会替换新的对象或加载状态。</summary>
    [Fact]
    public async Task SelectEntry_IgnoresLateDetailsAndKeepsMetadata()
    {
        var oldDetails = new TaskCompletionSource<ObjectStorageObjectDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("old.json", false), new("new.json", false)])),
            DetailsHandler = (_, _, key, token) =>
            {
                if (key == "new.json") return Task.FromResult(new ObjectStorageObjectDetails(key, 2048, null, "Standard", "application/json", "私有", "{\"new\":true}"));
                oldToken = token;
                return oldDetails.Task;
            }
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var old = model.SelectEntryAsync(model.Entries.Single(entry => entry.Key == "old.json"), TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(entry => entry.Key == "new.json"), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(["old.json", "new.json"], service.DetailsCalls);
        oldDetails.SetResult(new("old.json", 100, null, "Standard", "text/plain", "public-read", "old"));
        await old.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(oldToken.IsCancellationRequested);
        Assert.Equal("new.json", model.Details?.Key);
        Assert.Equal("2 KB", model.DetailSize);
        Assert.Equal("私有", model.DetailAccess);
        Assert.Equal("{\"new\":true}", model.PreviewText);
        Assert.False(model.IsDetailLoading);
    }

    /// <summary>关闭页签取消详情读取并忽略服务迟到返回。</summary>
    [Fact]
    public async Task Close_CancelsAndIgnoresLatePreview()
    {
        var delayed = new TaskCompletionSource<ObjectStorageObjectDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken readToken = default;
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("config.json", false)])),
            DetailsHandler = (_, _, _, token) => { readToken = token; return delayed.Task; }
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var details = model.SelectEntryAsync(model.Entries[0], TestContext.Current.CancellationToken);
        await model.CloseAsync();
        delayed.SetResult(new("config.json", 3, null, "Standard", "text/plain", "私有", "late"));
        await details;
        Assert.True(readToken.IsCancellationRequested);
        Assert.Null(model.Details);
        Assert.Empty(model.PreviewText);
        Assert.False(model.DownloadCommand.CanExecute(null));
    }

    /// <summary>替换服务返回过大文本时仍遵守 UTF-8 的 256KB 预览上限。</summary>
    [Fact]
    public async Task OversizedPreview_IsNotSentToTextControl()
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("huge.txt", false)])),
            DetailsHandler = (_, _, key, _) => Task.FromResult(new ObjectStorageObjectDetails(key, 400000, null,
                "Standard", "text/plain", "私有", new string('中', 100000)))
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries[0], TestContext.Current.CancellationToken);
        Assert.False(model.HasTextPreview);
        Assert.Empty(model.PreviewText);
        Assert.Contains("256KB", model.PreviewMessage, StringComparison.Ordinal);
        Assert.True(model.DownloadCommand.CanExecute(null));
    }

    /// <summary>失败消息经过安全分流，详情失败不清空可继续浏览的列表。</summary>
    [Fact]
    public async Task DetailFailure_KeepsListAndDoesNotExposeRawException()
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("config.json", false)])),
            DetailsHandler = (_, _, _, _) => throw new InvalidOperationException("access-key=hidden")
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries[0], TestContext.Current.CancellationToken);
        Assert.Single(model.Entries);
        Assert.Empty(model.ReadError);
        Assert.DoesNotContain("access-key", model.DetailError, StringComparison.Ordinal);
        Assert.Contains("无法读取", model.PreviewMessage, StringComparison.Ordinal);
        Assert.True(model.DownloadCommand.CanExecute(null));
    }
}
