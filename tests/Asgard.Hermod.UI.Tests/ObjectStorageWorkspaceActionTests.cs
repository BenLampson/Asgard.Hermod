namespace Asgard.Hermod.UI.Tests;

/// <summary>验证确认前不发写请求、完整删除范围、覆盖竞态和失败重试。</summary>
public sealed class ObjectStorageWorkspaceActionTests
{
    /// <summary>目录删除先展开完整键清单，取消后没有删除请求。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteDirectory_UsesExactlyConfirmedPlan(bool confirm)
    {
        var plan = new ObjectStorageDeletePlan(["docs/a.txt", "docs/nested/b.txt", "docs/"]);
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("docs/", true)])),
            PrepareDeleteHandler = (_, _) => Task.FromResult(plan)
        };
        var dialogs = new ObjectStorageTestDialogs { ConfirmDeletion = confirm };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.Entries[0].IsChecked = true;
        await model.DeleteCommand.ExecuteAsync();

        Assert.Same(plan, dialogs.PresentedPlan);
        Assert.Equal(confirm ? 1 : 0, service.DeletedPlans.Count);
        if (confirm) Assert.Same(plan, Assert.Single(service.DeletedPlans));
    }

    /// <summary>默认同名策略在确认前不覆盖，取消明确留下跳过结果。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadAsk_RequiresExplicitOverwriteConfirmation(bool overwrite)
    {
        var service = new ObjectStorageTestService { ExistsHandler = (_, _) => Task.FromResult(true) };
        var dialogs = new ObjectStorageTestDialogs
        {
            UploadSelection = new([new("chosen.txt", "chosen.txt", 100)]),
            ConfirmOverwrite = overwrite
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.UploadCommand.ExecuteAsync();

        Assert.Equal(1, dialogs.OverwriteConfirmations);
        var task = Assert.Single(model.Transfers);
        Assert.Equal(overwrite ? "已完成" : "已跳过", task.State);
        Assert.Equal(overwrite ? 1 : 0, service.UploadCalls.Count);
        if (overwrite) Assert.True(service.UploadCalls[0].Overwrite);
    }

    /// <summary>同名检查之后新出现的对象必须再确认，Skip 不会覆盖竞态对象。</summary>
    [Theory]
    [InlineData(ObjectStorageOverwritePolicy.Ask, true)]
    [InlineData(ObjectStorageOverwritePolicy.Ask, false)]
    [InlineData(ObjectStorageOverwritePolicy.Skip, false)]
    public async Task UploadRace_DoesNotSilentlyOverwrite(ObjectStorageOverwritePolicy policy, bool confirm)
    {
        var service = new ObjectStorageTestService
        {
            UploadHandler = (_, overwrite, _, _) => !overwrite
                ? throw new ObjectStorageException("同名对象已存在", "ObjectAlreadyExists") : Task.CompletedTask
        };
        var dialogs = new ObjectStorageTestDialogs
        {
            UploadSelection = new([new("a.txt", "a.txt", 10)], policy),
            ConfirmOverwrite = confirm
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.UploadCommand.ExecuteAsync();

        var task = Assert.Single(model.Transfers);
        Assert.Equal(confirm ? "已完成" : "已跳过", task.State);
        Assert.Equal(confirm ? 2 : 1, service.UploadCalls.Count);
        Assert.False(service.UploadCalls[0].Overwrite);
        Assert.Equal(policy == ObjectStorageOverwritePolicy.Skip ? 0 : 1, dialogs.OverwriteConfirmations);
    }

    /// <summary>无法检查同名对象时不能当作不存在，也不能发出上传请求。</summary>
    [Fact]
    public async Task UploadExistsFailure_KeepsFailedTaskAndRetryRechecks()
    {
        int attempts = 0;
        var service = new ObjectStorageTestService
        {
            ExistsHandler = (_, _) => ++attempts == 1 ? throw new ObjectStorageException("没有读取对象权限", "AccessDenied") : Task.FromResult(false)
        };
        var dialogs = new ObjectStorageTestDialogs { UploadSelection = new([new("a.txt", "a.txt", 10)]) };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.UploadCommand.ExecuteAsync();
        var task = Assert.Single(model.Transfers);
        Assert.True(task.IsFailed);
        Assert.Contains("AccessDenied", task.Error, StringComparison.Ordinal);
        Assert.Empty(service.UploadCalls);

        await task.RetryCommand.ExecuteAsync();
        Assert.True(task.IsCompleted);
        Assert.Equal(2, attempts);
        Assert.Single(service.UploadCalls);
        Assert.Single(model.Transfers);
    }

    /// <summary>打开临时链接窗口不会自动生成，只有用户操作回调才调用服务。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporaryLink_OnlyGeneratesOnUserCallback(bool generate)
    {
        int generated = 0;
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("private.json", false)])),
            LinkHandler = (_, lifetime, _) => { generated++; Assert.Equal(TimeSpan.FromMinutes(15), lifetime); return Task.FromResult("https://example.invalid/private"); }
        };
        var dialogs = new ObjectStorageTestDialogs { GenerateLifetime = generate ? TimeSpan.FromMinutes(15) : null };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.TemporaryLinkCommand.ExecuteAsync(model.Entries[0]);
        Assert.Equal(generate ? 1 : 0, generated);
        Assert.Empty(dialogs.CopiedTexts);
    }

    /// <summary>复制行为只能来自用户命令，单击读取详情不会写剪贴板。</summary>
    [Fact]
    public async Task CopyKey_RequiresCommandAndUsesCompleteKey()
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("releases/config.json", false)]))
        };
        var dialogs = new ObjectStorageTestDialogs();
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries[0], TestContext.Current.CancellationToken);
        Assert.Empty(dialogs.CopiedTexts);
        model.CopyKeyCommand.Execute(null);
        Assert.Equal("releases/config.json", Assert.Single(dialogs.CopiedTexts));
    }

    /// <summary>批量下载目录展开期间切换浏览前缀，不改变已选择的本地相对结构。</summary>
    [Fact]
    public async Task BatchDownload_CapturesOriginalPrefixAcrossDirectoryChange()
    {
        var delayed = new TaskCompletionSource<ObjectStoragePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? downloadPath = null;
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, prefix, _, _, _) => prefix == "original/docs/" ? delayed.Task
                : Task.FromResult(new ObjectStoragePage(prefix == "original/" ? [new("original/docs/", true)] : [])),
            DownloadHandler = (_, path, _, _) => { downloadPath = path; return Task.CompletedTask; }
        };
        var dialogs = new ObjectStorageTestDialogs { DownloadFolder = "C:\\HermodDownloadTests" };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.NavigateAsync(model.Buckets[0], "original/", TestContext.Current.CancellationToken);
        model.Entries[0].IsChecked = true;
        var download = model.BatchDownloadCommand.ExecuteAsync();
        await model.NavigateAsync(model.Buckets[0], "other/", TestContext.Current.CancellationToken);
        delayed.SetResult(new([new("original/docs/a.txt", false)]));
        await download;

        Assert.Equal("C:\\HermodDownloadTests\\docs\\a.txt", downloadPath);
        Assert.Equal("original/docs/a.txt", Assert.Single(model.Transfers).Key);
    }

    /// <summary>关闭先取消传输，并等待模拟远端中止清理后才允许宿主释放客户端。</summary>
    [Fact]
    public async Task Close_WaitsForTransferAbortCleanup()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ObjectStorageTestService
        {
            UploadHandler = async (_, _, _, token) =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { cancelled.SetResult(); await cleanup.Task.WaitAsync(TestContext.Current.CancellationToken); }
            }
        };
        var dialogs = new ObjectStorageTestDialogs { UploadSelection = new([new("a.txt", "a.txt", 10)]) };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.UploadCommand.ExecuteAsync();
        Assert.True(Assert.Single(model.Transfers).IsRunning);

        var close = model.CloseAsync();
        await cancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(model.IsClosed);
        Assert.False(close.IsCompleted);
        cleanup.SetResult();
        await close;
        Assert.True(Assert.Single(model.Transfers).Completion.IsCompleted);
    }
}
